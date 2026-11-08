using Markd.Core.Domain;
using Markd.Core.Sync;
using Microsoft.Extensions.Logging;

namespace Markd.Services.Sync;

public enum SyncTrigger { Start, Resume, LocalChange, Timer, Manual, Connectivity, Apply }

/// <summary>
/// Runs syncs for the whole app (singleton): one at a time, triggers during a run coalesce into one follow-up.
/// Maps outcomes to <see cref="Status"/> and walks the user through conflicts. Core calls run off the UI thread;
/// every seam call and <see cref="StatusChanged"/> goes through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class SyncCoordinator(
    ISyncSession session,
    ICloudProviderCatalog catalog,
    IConflictResolutionService conflictResolver,
    ISyncDataRefresher refresher,
    IUiDispatcher dispatcher,
    ILogger<SyncCoordinator> logger)
{
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

    public SyncStatus Status => _status;

    public event EventHandler? StatusChanged;

    public IReadOnlyList<SyncConflict> PendingConflicts => _pendingConflicts;

    /// <summary>Whether <see cref="SyncState.IsConnected"/> was true at the last read.</summary>
    public bool IsLinked => _isLinked;

    /// <summary>Builds <see cref="Status"/> from the stored sync state, without touching the network.</summary>
    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            await SetStatusAsync(StatusFromStoredState(state));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Syncs now, or queues one follow-up when a run is in progress. Automatic triggers are ignored when the device is
    /// not linked or waits for the user; <see cref="SyncTrigger.Manual"/>, <see cref="SyncTrigger.Start"/> and
    /// <see cref="SyncTrigger.Apply"/> always run when linked. Completes when the run (and any follow-up) finished.
    /// </summary>
    public Task SyncNowAsync(SyncTrigger trigger)
    {
        TaskCompletionSource done;
        lock (_lock)
        {
            if (!ShouldRun(trigger))
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

    /// <summary>Shows the conflict list for <see cref="PendingConflicts"/>, syncing first when none are known yet.</summary>
    public Task OpenConflictsAsync() =>
        PendingConflicts.Count == 0 ? SyncNowAsync(SyncTrigger.Manual) : ResolveConflictsAsync();

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
            _pendingConflicts = [];
            await SetStatusAsync(OffStatus);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunLoopAsync(SyncTrigger trigger, TaskCompletionSource done)
    {
        try
        {
            SyncTrigger? next = trigger;
            while (next is { } current)
            {
                if (ShouldRun(current))
                    await SyncAndResolveAsync();

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
        catch (Exception ex)
        {
            lock (_lock)
            {
                _followUp = null;
                _loopDone = null;
            }

            done.SetException(ex);
            throw;
        }
    }

    private async Task SyncAndResolveAsync()
    {
        var result = await RunAsync((sync, provider) => sync.SyncAsync(provider));
        if (result?.Outcome == SyncOutcome.NeedsConflictResolution)
            await ResolveConflictsAsync();
    }

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
    /// Runs one core operation under the gate: re-reads the sync state, drops the run when the device is not linked
    /// (unless <paramref name="connectingTo"/> is given, for a first run that links it) or this build has no such
    /// provider, refreshes the UI when local data changed, and updates <see cref="Status"/>. Null when dropped.
    /// </summary>
    private async Task<SyncResult?> RunAsync(
        Func<ISyncService, ICloudSyncProvider, Task<SyncResult>> operation, ICloudSyncProvider? connectingTo = null)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadStateAsync();
            _isLinked = state.IsConnected;
            var provider = connectingTo ?? (state.IsConnected && state.ProviderId is { } id ? catalog.Get(id) : null);
            if (provider is null)
            {
                _pendingConflicts = [];
                await SetStatusAsync(OffStatus);
                return null;
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
                await SetStatusAsync(StatusFromStoredState(stored));
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

            var kind = KindOf(result.Outcome);
            await SetStatusAsync(kind == SyncStatusKind.Off
                ? OffStatus
                : BuildStatus(kind, after, after.ProviderId ?? provider.Id, _pendingConflicts.Count));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool ShouldRun(SyncTrigger trigger) =>
        IsForced(trigger) || (_isLinked && !NeedsUser(_status.Kind));

    private static bool IsForced(SyncTrigger trigger) =>
        trigger is SyncTrigger.Manual or SyncTrigger.Start or SyncTrigger.Apply;

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
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        });
    }
}
