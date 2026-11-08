using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Markd.Services.Sync;

/// <summary>
/// What asked for a sync. <see cref="Apply"/> stands for the run that applies conflict answers, which only the
/// coordinator makes from its conflict list: <see cref="SyncCoordinator.SyncNowAsync"/> ignores it.
/// </summary>
public enum SyncTrigger { Start, Resume, LocalChange, Timer, Manual, Connectivity, Apply }

/// <summary>
/// Runs syncs for the whole app (singleton): one at a time, triggers during a run coalesce into one follow-up.
/// Maps outcomes to <see cref="Status"/> and walks the user through conflicts, connecting, passphrases, replacing the
/// cloud data and disconnecting. Core calls run off the UI thread; every seam call and <see cref="StatusChanged"/>
/// goes through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class SyncCoordinator(
    ISyncSession session,
    ICloudProviderCatalog catalog,
    IConflictResolutionService conflictResolver,
    ISyncDataRefresher refresher,
    IUiDispatcher dispatcher,
    ISyncSecretStore secrets,
    ISyncPassphrasePromptService passphrasePrompt,
    IAppShellService shell,
    IFeedbackService feedback,
    ILogger<SyncCoordinator> logger) : ISyncController, ISyncLink
{
    private enum UnlockResult { Unlocked, Cancelled, ForgotIt, Failed }

    private static readonly SyncStatus OffStatus = new(SyncStatusKind.Off, null, 0, null, null);

    private readonly object _lock = new();

    // Serializes every core call: the sync session has one DbContext.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Guarded by _lock.
    private TaskCompletionSource? _loopDone;
    private SyncTrigger? _followUp;
    private bool _listOpen;

    private volatile SyncStatus _status = OffStatus;
    private volatile bool _isLinked;
    private volatile IReadOnlyList<SyncConflict> _pendingConflicts = [];

    // The provider a connect is linking to, until the device is linked or the connect is abandoned.
    private volatile ICloudSyncProvider? _connecting;

    public SyncStatus Status => _status;

    public event EventHandler? StatusChanged;

    public IReadOnlyList<SyncConflict> PendingConflicts => _pendingConflicts;

    /// <summary>Whether <see cref="SyncState.IsConnected"/> was true at the last read.</summary>
    public bool IsLinked => _isLinked;

    /// <summary>Builds <see cref="Status"/> from the stored sync state, without touching the network. Never throws.</summary>
    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            _pendingConflicts = [];
            await SetStatusAsync(StatusFromStoredState(state));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reading the sync state failed");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Syncs now, or queues one follow-up when a run is in progress. Automatic triggers are ignored when the device is
    /// not linked or waits for the user; <see cref="SyncTrigger.Manual"/> and <see cref="SyncTrigger.Start"/> always run
    /// when linked, and <see cref="SyncTrigger.Apply"/> is ignored. Completes when the run (and any follow-up) finished.
    /// Never throws: failures show as <see cref="SyncStatusKind.Failed"/>.
    /// </summary>
    public Task SyncNowAsync(SyncTrigger trigger)
    {
        TaskCompletionSource done;
        lock (_lock)
        {
            if (trigger == SyncTrigger.Apply || !ShouldRun(trigger))
                return Task.CompletedTask;

            if (_loopDone is { } running)
            {
                _followUp = Stronger(_followUp, trigger);
                return running.Task;
            }

            done = _loopDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        return RunLoopAsync(trigger, done);
    }

    /// <summary>
    /// Shows the conflict list for <see cref="PendingConflicts"/>, syncing first when none are known yet. Never throws.
    /// </summary>
    public async Task OpenConflictsAsync()
    {
        if (PendingConflicts.Count == 0)
        {
            await SyncNowAsync(SyncTrigger.Manual);
            return;
        }

        try
        {
            await ResolveConflictsAsync();
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
        }
    }

    /// <summary>
    /// Links this device to a provider's cloud file. No file yet: asks for a new passphrase and stores it. A file:
    /// asks for its passphrase until it opens the file (nothing is stored or uploaded before that), or leads to
    /// <see cref="ReplaceCloudAsync"/> when the user forgot it. Cancelling (a prompt, the replace confirm or the first
    /// run) clears the stored passphrase. Then runs the first sync, which links the device. A link to a provider this
    /// build does not have is forgotten first. Never throws.
    /// </summary>
    public async Task ConnectAsync(string providerId)
    {
        if (catalog.Get(providerId) is not { } provider)
            return;

        try
        {
            await ForgetStaleLinkAsync();

            var info = await Task.Run(() => provider.GetInfoAsync(CancellationToken.None));
            if (info is null)
            {
                if ((await PromptAsync(PassphrasePromptKind.CreateNew, false)).Passphrase is not { } created)
                {
                    await CancelConnectAsync();
                    return;
                }

                await secrets.SetPassphraseAsync(created);
            }
            else
            {
                switch (await UnlockWithPromptAsync(provider, PassphrasePromptKind.EnterExisting))
                {
                    case UnlockResult.Cancelled:
                        await CancelConnectAsync();
                        return;
                    case UnlockResult.ForgotIt:
                        if (!await ReplaceCloudCoreAsync(provider))
                            await CancelConnectAsync();
                        return;
                    case UnlockResult.Failed:
                        if (!_isLinked)
                            await CancelConnectAsync();
                        return;
                }
            }

            // Until the device is linked, every run (the conflict apply and a later Resolve included) goes to this
            // provider. RunAsync forgets it once linked, or abandons the connect when a run neither links nor stops
            // at conflicts.
            _connecting = provider;
            await SyncAndResolveAsync();
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Connecting was cancelled");
            try
            {
                await CancelConnectAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cancelling the connect failed");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Connecting to {Provider} failed", providerId);
            if (!_isLinked)
                _connecting = null;
            await ShowFeedbackAsync(Strings.Sync_ConnectFailed, FailureDetail(ex));
        }
    }

    /// <summary>
    /// For a passphrase changed on another device: asks for it until it opens the cloud file, then syncs. Never throws.
    /// </summary>
    public async Task EnterPassphraseAsync()
    {
        try
        {
            if (await LinkedProviderAsync() is not { } provider)
                return;

            switch (await UnlockWithPromptAsync(provider, PassphrasePromptKind.EnterChanged))
            {
                case UnlockResult.ForgotIt:
                    await ReplaceCloudCoreAsync(null);
                    return;
                case UnlockResult.Unlocked:
                    break;
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
            return;
        }

        await SyncNowAsync(SyncTrigger.Manual);
    }

    /// <summary>
    /// Re-encrypts the cloud data under a new passphrase. When the cloud took it but this device could not store it,
    /// the status asks for the passphrase and a hint says why. Never throws.
    /// </summary>
    public async Task ChangePassphraseAsync()
    {
        try
        {
            if ((await PromptAsync(PassphrasePromptKind.ChangeOwn, false)).Passphrase is not { } passphrase)
                return;

            var result = await RunAsync((sync, provider) => sync.ChangePassphraseAsync(provider, passphrase));
            if (result is null)
                return;

            if (IsPassphraseNotStored(result))
                await ShowFeedbackAsync(Strings.Sync_StatusNeedsPassphrase, Strings.Sync_PassphraseNotStored);
            else if (result.Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges)
                await ShowFeedbackAsync(Strings.Sync_PassphraseChanged);
            else if (result.Outcome == SyncOutcome.NeedsConflictResolution)
                await ResolveConflictsAsync();
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
        }
    }

    /// <summary>
    /// Overwrites the cloud data with this device's under a new passphrase, after a destructive confirm that says the
    /// other devices add their own events back. For a forgotten passphrase or unreadable cloud data. Never throws.
    /// </summary>
    public async Task ReplaceCloudAsync()
    {
        try
        {
            await ReplaceCloudCoreAsync(null);
        }
        catch (Exception ex)
        {
            await ShowFailureAsync(ex);
        }
    }

    /// <summary>
    /// Disconnects after a confirm, then asks separately whether to delete the cloud file too (default: keep it).
    /// Local data is never touched. Never throws.
    /// </summary>
    public async Task DisconnectAsync()
    {
        try
        {
            if (!await ConfirmAsync(Strings.Sync_DisconnectConfirmTitle, Strings.Sync_DisconnectConfirmMessage,
                    Strings.Sync_Disconnect, Strings.Common_Cancel, destructive: false))
                return;

            var deleteRemote = await ConfirmAsync(Strings.Sync_DeleteCloudTitle, Strings.Sync_DeleteCloudMessage,
                Strings.Sync_DeleteCloud, Strings.Sync_KeepCloud, destructive: true);

            SyncResult result;
            await _gate.WaitAsync();
            try
            {
                var state = await LoadStateAsync();
                var provider = (state.ProviderId is { } id ? catalog.Get(id) : null) ?? _connecting;
                result = await Task.Run(() => session.Sync.DisconnectAsync(provider, deleteRemote));
                _isLinked = (await LoadStateAsync()).IsConnected;
                if (result.Outcome == SyncOutcome.Disconnected)
                {
                    _connecting = null;
                    _pendingConflicts = [];
                    await SetStatusAsync(OffStatus);
                }
            }
            finally
            {
                _gate.Release();
            }

            if (result.Outcome != SyncOutcome.Disconnected)
            {
                logger.LogWarning("Disconnecting failed: {Outcome} {Detail}", result.Outcome, result.Detail);
                await ShowFeedbackAsync(Strings.Sync_DisconnectFailed);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Disconnecting failed");
            await ShowFeedbackAsync(Strings.Sync_DisconnectFailed);
        }
    }

    /// <summary>True when a sync state row with a provider exists (linked, or disconnected with a kept base).</summary>
    public async Task<bool> HasSyncStateAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _isLinked = (await LoadStateAsync()).IsConnected;
            return await Task.Run(() => session.State.HasSyncStateAsync());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs a manual sync and tells whether it ended synced. Never throws.</summary>
    public async Task<bool> TrySyncNowAsync()
    {
        await SyncNowAsync(SyncTrigger.Manual);
        return _status.Kind == SyncStatusKind.Idle;
    }

    /// <summary>
    /// Forgets the sync link before an erase (<see cref="ISyncService.ForgetLinkAsync"/>). Throws when it did not
    /// disconnect, so the caller never purges a device that is still linked.
    /// </summary>
    public async Task ForgetLinkForEraseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var result = await Task.Run(() => session.Sync.ForgetLinkAsync());
            if (result.Outcome != SyncOutcome.Disconnected)
                throw new InvalidOperationException($"Forgetting the sync link failed: {result.Outcome} {result.Detail}");

            _isLinked = false;
            _connecting = null;
            _pendingConflicts = [];
            await SetStatusAsync(OffStatus);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs the trigger and then each queued follow-up. A failing run is logged and shown as Failed; it never faults
    /// the callers waiting on this loop and never drops the follow-up.
    /// </summary>
    private async Task RunLoopAsync(SyncTrigger trigger, TaskCompletionSource done)
    {
        SyncTrigger? next = trigger;
        while (next is { } current)
        {
            try
            {
                if (ShouldRun(current))
                    await SyncAndResolveAsync();
            }
            catch (Exception ex)
            {
                await ShowFailureAsync(ex);
            }

            lock (_lock)
            {
                next = _followUp;
                _followUp = null;
                if (next is null)
                    _loopDone = null;
            }
        }

        done.SetResult();
    }

    /// <summary>
    /// After an unexpected exception (not a core sync failure, which is a result): log it and never leave the status
    /// at Syncing. A cancelled run already restored its status.
    /// </summary>
    private async Task ShowFailureAsync(Exception ex)
    {
        if (ex is OperationCanceledException && _status.Kind != SyncStatusKind.Syncing)
        {
            logger.LogInformation("Sync was cancelled");
            return;
        }

        logger.LogError(ex, "Sync failed unexpectedly");
        if (_status.Kind == SyncStatusKind.Off)
            return;

        _pendingConflicts = [];
        try
        {
            await SetStatusAsync(_status with { Kind = SyncStatusKind.Failed, ConflictCount = 0 });
        }
        catch (Exception statusEx)
        {
            logger.LogError(statusEx, "Showing the sync failure failed");
        }
    }

    private async Task SyncAndResolveAsync()
    {
        var result = await RunAsync((sync, provider) => sync.SyncAsync(provider));
        if (result?.Outcome == SyncOutcome.NeedsConflictResolution)
            await ResolveConflictsAsync();
    }

    /// <summary>
    /// The destructive confirm, a new passphrase, then the replace. With <paramref name="connectingTo"/> it is the
    /// first run of a connect. False when the user cancelled the confirm or the prompt.
    /// </summary>
    private async Task<bool> ReplaceCloudCoreAsync(ICloudSyncProvider? connectingTo)
    {
        if (!await ConfirmAsync(Strings.Sync_ReplaceConfirmTitle, Strings.Sync_ReplaceConfirmMessage,
                Strings.Sync_ActionReplaceCloud, Strings.Common_Cancel, destructive: true))
            return false;

        if ((await PromptAsync(PassphrasePromptKind.CreateNew, false)).Passphrase is not { } passphrase)
            return false;

        if (connectingTo is not null)
            _connecting = connectingTo;
        await RunAsync((sync, provider) => sync.ReplaceRemoteAsync(provider, passphrase));
        return true;
    }

    /// <summary>
    /// Asks for the passphrase of an existing cloud file until <see cref="ISyncService.UnlockAsync"/> accepts it,
    /// showing the previous failure on each retry. Any outcome other than a wrong passphrase ends the loop. When the
    /// file is gone (<see cref="SyncOutcome.NoCloudFile"/>), the typed passphrase proved nothing: asks for a new one
    /// typed twice and stores that.
    /// </summary>
    private async Task<UnlockResult> UnlockWithPromptAsync(ICloudSyncProvider provider, PassphrasePromptKind kind)
    {
        var failed = false;
        while (true)
        {
            var answer = await PromptAsync(kind, failed);
            if (answer.ForgotIt)
                return UnlockResult.ForgotIt;
            if (answer.Passphrase is not { } passphrase)
                return UnlockResult.Cancelled;

            SyncResult result;
            await _gate.WaitAsync();
            try
            {
                result = await Task.Run(() => session.Sync.UnlockAsync(provider, passphrase));
            }
            finally
            {
                _gate.Release();
            }

            switch (result.Outcome)
            {
                case SyncOutcome.NoChanges or SyncOutcome.Synced:
                    return UnlockResult.Unlocked;
                case SyncOutcome.NoCloudFile:
                    if ((await PromptAsync(PassphrasePromptKind.CreateNew, false)).Passphrase is not { } created)
                        return UnlockResult.Cancelled;
                    await secrets.SetPassphraseAsync(created);
                    return UnlockResult.Unlocked;
                case SyncOutcome.WrongPassphrase:
                    failed = true;
                    continue;
                default:
                    logger.LogWarning("Unlocking failed: {Outcome} {Detail}", result.Outcome, result.Detail);
                    await ShowFeedbackAsync(Strings.Sync_ConnectFailed, FailureDetail(result.Outcome));
                    return UnlockResult.Failed;
            }
        }
    }

    /// <summary>Why a connect or unlock failed, in the user's words.</summary>
    private static string FailureDetail(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.Offline => Strings.Sync_FailOffline,
        SyncOutcome.RemoteUnreadable => Strings.Sync_StatusUnreadable,
        SyncOutcome.RemoteTooNew => Strings.Sync_StatusUpdateApp,
        SyncOutcome.AuthExpired => Strings.Sync_StatusSignInAgain,
        SyncOutcome.WrongPassphrase or SyncOutcome.NeedsPassphrase => Strings.Sync_FailWrongPassphrase,
        _ => Strings.Backup_UnexpectedError,
    };

    private static string FailureDetail(Exception ex) => ex switch
    {
        SyncOfflineException => FailureDetail(SyncOutcome.Offline),
        SyncAuthException => FailureDetail(SyncOutcome.AuthExpired),
        _ => Strings.Backup_UnexpectedError,
    };

    /// <summary>
    /// A cancelled connect on a device that is not linked leaves nothing behind: no provider being connected (an
    /// earlier connect that stopped at conflicts included), no passphrase, and the status shows Off. A linked device
    /// keeps its link and its passphrase.
    /// </summary>
    private async Task CancelConnectAsync()
    {
        if (_isLinked)
            return;

        _connecting = null;
        _pendingConflicts = [];
        await secrets.SetPassphraseAsync(null);
        await SetStatusAsync(OffStatus);
    }

    /// <summary>
    /// A device linked to a provider this build does not have shows Off and drops every run, so a connect would be
    /// silently ignored: forget that link before connecting.
    /// </summary>
    private async Task ForgetStaleLinkAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            if (!state.IsConnected || (state.ProviderId is { } id && catalog.Get(id) is not null))
                return;

            var result = await Task.Run(() => session.Sync.ForgetLinkAsync());
            if (result.Outcome != SyncOutcome.Disconnected)
                throw new InvalidOperationException($"Forgetting the stale sync link failed: {result.Outcome} {result.Detail}");

            _isLinked = false;
            _pendingConflicts = [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ICloudSyncProvider?> LinkedProviderAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            return state.IsConnected && state.ProviderId is { } id ? catalog.Get(id) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PassphrasePromptResult> PromptAsync(PassphrasePromptKind kind, bool previousAttemptFailed)
    {
        var result = new PassphrasePromptResult(null, false);
        await dispatcher.InvokeAsync(async () => result = await passphrasePrompt.PromptAsync(kind, previousAttemptFailed));
        return result;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel, bool destructive)
    {
        var confirmed = false;
        await dispatcher.InvokeAsync(async () =>
            confirmed = await shell.DisplayAlertAsync(title, message, accept, cancel, destructive));
        return confirmed;
    }

    private Task ShowFeedbackAsync(string title, string? detail = null) =>
        dispatcher.InvokeAsync(() => feedback.ShowAsync(title, detail));

    private static bool IsPassphraseNotStored(SyncResult result) =>
        result is { Outcome: SyncOutcome.Failed, Detail: SyncResult.PassphraseNotStoredDetail };

    /// <summary>
    /// The conflict loop: show the list, apply the answers, and show it again when applying finds new conflicts.
    /// Only one list is open at a time; a run that finds conflicts meanwhile just updates <see cref="PendingConflicts"/>.
    /// </summary>
    private async Task ResolveConflictsAsync()
    {
        lock (_lock)
        {
            if (_listOpen)
                return;
            _listOpen = true;
        }

        try
        {
            while (PendingConflicts is { Count: > 0 } conflicts)
            {
                IReadOnlyList<ConflictResolution>? answers = null;
                await dispatcher.InvokeAsync(async () => answers = await conflictResolver.ResolveAsync(conflicts));
                if (answers is null)
                    return;   // Later: the status stays Conflicts

                var result = await RunAsync((sync, provider) => sync.SyncAsync(provider, answers));
                if (result?.Outcome != SyncOutcome.NeedsConflictResolution)
                    return;
            }
        }
        finally
        {
            lock (_lock)
                _listOpen = false;
        }
    }

    /// <summary>
    /// Runs one core operation under the gate: re-reads the sync state, drops the run when the device is neither
    /// linked nor being connected (<see cref="_connecting"/>) or this build has no such provider, refreshes the UI
    /// when local data changed, and updates <see cref="Status"/>. Null when dropped. A connect run that links the
    /// device ends the connect; one that neither links nor stops at conflicts abandons it: no passphrase is left
    /// behind, the status is Off and the cause is shown.
    /// </summary>
    private async Task<SyncResult?> RunAsync(Func<ISyncService, ICloudSyncProvider, Task<SyncResult>> operation)
    {
        var result = await RunUnderGateAsync(operation);
        if (result is { Abandoned: true, Result: { } failed })
            await ShowFeedbackAsync(Strings.Sync_ConnectFailed, FailureDetail(failed.Outcome));
        return result.Result;
    }

    private async Task<(SyncResult? Result, bool Abandoned)> RunUnderGateAsync(
        Func<ISyncService, ICloudSyncProvider, Task<SyncResult>> operation)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            var connecting = !state.IsConnected ? _connecting : null;
            var provider = connecting ?? (state.IsConnected && state.ProviderId is { } id ? catalog.Get(id) : null);
            if (provider is null)
            {
                _pendingConflicts = [];
                await SetStatusAsync(OffStatus);
                return (null, false);
            }

            await SetStatusAsync(BuildStatus(SyncStatusKind.Syncing, state, provider.Id, 0));

            SyncResult result;
            try
            {
                result = await Task.Run(() => operation(session.Sync, provider));
            }
            catch (OperationCanceledException)
            {
                // A cancelled run loses LocalDataChanged: it may have applied data before stopping.
                await RefreshAsync();
                var stored = await LoadStateAsync();
                _isLinked = stored.IsConnected;
                var restored = StatusFromStoredState(stored);
                if (restored.Kind == SyncStatusKind.Conflicts)
                    restored = restored with { ConflictCount = _pendingConflicts.Count };
                else
                    _pendingConflicts = [];
                await SetStatusAsync(restored);
                throw;
            }
            catch (Exception ex)
            {
                // ISyncService never throws for sync failures; this is a bug, so show Failed and refresh in case.
                logger.LogError(ex, "Sync operation threw");
                result = new SyncResult(SyncOutcome.Failed, Detail: ex.Message) { LocalDataChanged = true };
            }

            if (result.LocalDataChanged)
                await RefreshAsync();

            var after = await LoadStateAsync();
            _isLinked = after.IsConnected;
            _pendingConflicts = result.Outcome == SyncOutcome.NeedsConflictResolution ? result.Conflicts ?? [] : [];

            if (after.IsConnected)
            {
                _connecting = null;
            }
            else if (connecting is not null && result.Outcome != SyncOutcome.NeedsConflictResolution)
            {
                logger.LogWarning("Connecting failed: {Outcome} {Detail}", result.Outcome, result.Detail);
                _connecting = null;
                await secrets.SetPassphraseAsync(null);
                await SetStatusAsync(OffStatus);
                return (result, true);
            }

            // The cloud took a new passphrase this device could not store: the user has to enter it again.
            var kind = IsPassphraseNotStored(result) ? SyncStatusKind.NeedsPassphrase : KindOf(result.Outcome);
            await SetStatusAsync(kind == SyncStatusKind.Off
                ? OffStatus
                : BuildStatus(kind, after, after.ProviderId ?? provider.Id, _pendingConflicts.Count));
            return (result, false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool ShouldRun(SyncTrigger trigger) =>
        IsForced(trigger) || (_isLinked && !NeedsUser(_status.Kind));

    private static bool IsForced(SyncTrigger trigger) =>
        trigger is SyncTrigger.Manual or SyncTrigger.Start;

    private static bool NeedsUser(SyncStatusKind kind) => kind is SyncStatusKind.Conflicts or SyncStatusKind.NeedsPassphrase
        or SyncStatusKind.SignInAgain or SyncStatusKind.UpdateApp or SyncStatusKind.Unreadable;

    /// <summary>The follow-up keeps the first queued trigger unless a later one would run where it would not.</summary>
    private static SyncTrigger Stronger(SyncTrigger? queued, SyncTrigger incoming) =>
        queued is not { } q || (IsForced(incoming) && !IsForced(q)) ? incoming : q;

    private static SyncStatusKind KindOf(SyncOutcome outcome) => outcome switch
    {
        SyncOutcome.Synced or SyncOutcome.NoChanges => SyncStatusKind.Idle,
        SyncOutcome.NeedsConflictResolution => SyncStatusKind.Conflicts,
        SyncOutcome.NeedsPassphrase or SyncOutcome.WrongPassphrase => SyncStatusKind.NeedsPassphrase,
        SyncOutcome.AuthExpired => SyncStatusKind.SignInAgain,
        SyncOutcome.Offline => SyncStatusKind.Offline,
        SyncOutcome.RemoteTooNew => SyncStatusKind.UpdateApp,
        SyncOutcome.RemoteUnreadable => SyncStatusKind.Unreadable,
        SyncOutcome.Disconnected => SyncStatusKind.Off,
        _ => SyncStatusKind.Failed,
    };

    /// <summary>
    /// The status before any run this session: from the last attempt's outcome. Conflicts from a previous session
    /// show with count 0 until the start run finds them again.
    /// </summary>
    private SyncStatus StatusFromStoredState(SyncState state)
    {
        if (!state.IsConnected || state.ProviderId is not { } providerId || catalog.Available.All(o => o.Id != providerId))
            return OffStatus;

        var kind = Enum.TryParse<SyncOutcome>(state.LastAttemptResult, out var outcome) ? KindOf(outcome) : SyncStatusKind.Idle;
        return kind == SyncStatusKind.Off ? OffStatus : BuildStatus(kind, state, providerId, 0);
    }

    private SyncStatus BuildStatus(SyncStatusKind kind, SyncState state, string providerId, int conflictCount) =>
        new(kind, state.LastSyncUtc, conflictCount,
            catalog.Available.FirstOrDefault(o => o.Id == providerId)?.DisplayName, state.AccountName);

    private Task<SyncState> LoadStateAsync() => Task.Run(() => session.State.LoadAsync());

    private async Task RefreshAsync()
    {
        try
        {
            await dispatcher.InvokeAsync(refresher.RefreshAfterSyncAsync);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Refreshing the UI after a sync failed");
        }
    }

    private Task SetStatusAsync(SyncStatus status)
    {
        if (status == _status)
            return Task.CompletedTask;

        _status = status;
        return dispatcher.InvokeAsync(() =>
        {
            // One failing subscriber must not stop the others or the run.
            foreach (var handler in StatusChanged?.GetInvocationList() ?? [])
            {
                try
                {
                    ((EventHandler)handler)(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "A sync status subscriber failed");
                }
            }

            return Task.CompletedTask;
        });
    }
}
