using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Sync;
using Markd.Services;
using Markd.Services.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Devices;
using Xunit;

namespace Markd.Tests;

public class SyncCoordinatorTests
{
    private const string ProviderId = "test";
    private static readonly DateTime LastSync = new(2026, 11, 1, 8, 30, 0, DateTimeKind.Utc);

    private readonly FakeStateStore _state = new();
    private readonly FakeSyncService _sync;
    private readonly FakeResolver _resolver = new();
    private readonly FakeRefresher _refresher = new();
    private readonly FakeDispatcher _dispatcher = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeSecrets _secrets = new();
    private readonly FakePassphrasePrompt _prompt = new();
    private readonly FakeShell _shell = new();
    private readonly FakeFeedback _feedback = new();

    public SyncCoordinatorTests() => _sync = new FakeSyncService(_state, _secrets);

    private SyncCoordinator Create()
    {
        _resolver.Dispatcher = _dispatcher;
        _refresher.Dispatcher = _dispatcher;
        _prompt.Dispatcher = _dispatcher;
        _shell.Dispatcher = _dispatcher;
        _resolver.SyncCallCount = () => _sync.SyncCalls.Count;
        return new SyncCoordinator(new FakeSession(_sync, _state), _catalog, _resolver, _refresher, _dispatcher,
            _secrets, _prompt, _shell, _feedback, NullLogger<SyncCoordinator>.Instance);
    }

    private void Link(string? lastAttempt = "Synced")
    {
        _state.Value.ProviderId = ProviderId;
        _state.Value.AccountName = "me@example.com";
        _state.Value.IsConnected = true;
        _state.Value.LastSyncUtc = LastSync;
        _state.Value.LastResult = "Synced";
        _state.Value.LastAttemptResult = lastAttempt;
    }

    private async Task<SyncCoordinator> CreateLinkedAsync(string? lastAttempt = "Synced")
    {
        Link(lastAttempt);
        var coordinator = Create();
        await coordinator.InitializeAsync();
        return coordinator;
    }

    private static CategoryConflict Conflict(string signature) =>
        new(Guid.NewGuid(), null, null, LastSync, LastSync) { Signature = signature };

    private static SyncResult ConflictResult(params SyncConflict[] conflicts) =>
        new(SyncOutcome.NeedsConflictResolution, conflicts);

    // ---- Status on init ----

    [Fact]
    public async Task Init_WithoutSyncState_IsOff()
    {
        var coordinator = Create();

        await coordinator.InitializeAsync();

        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
        Assert.Empty(_sync.SyncCalls);
    }

    [Fact]
    public async Task Init_Linked_IsIdleWithLastSyncTimeAndNames()
    {
        var coordinator = await CreateLinkedAsync();

        Assert.Equal(new SyncStatus(SyncStatusKind.Idle, LastSync, 0, "Test cloud", "me@example.com"), coordinator.Status);
        Assert.True(coordinator.IsLinked);
    }

    [Fact]
    public async Task Init_DisconnectedWithKeptBase_IsOff()
    {
        Link();
        _state.Value.IsConnected = false;
        var coordinator = Create();

        await coordinator.InitializeAsync();

        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
    }

    [Fact]
    public async Task Init_LastAttemptFailed_ShowsThatStatus()
    {
        var coordinator = await CreateLinkedAsync("Offline");

        Assert.Equal(SyncStatusKind.Offline, coordinator.Status.Kind);
        Assert.Equal(LastSync, coordinator.Status.LastSyncUtc);
    }

    [Fact]
    public async Task Init_ProviderMissingFromThisBuild_IsOffAndNothingRuns()
    {
        Link();
        _state.Value.ProviderId = "gone";
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.Empty(_sync.SyncCalls);
    }

    // ---- Outcome mapping ----

    [Theory]
    [InlineData(SyncOutcome.Synced, SyncStatusKind.Idle)]
    [InlineData(SyncOutcome.NoChanges, SyncStatusKind.Idle)]
    [InlineData(SyncOutcome.NeedsPassphrase, SyncStatusKind.NeedsPassphrase)]
    [InlineData(SyncOutcome.WrongPassphrase, SyncStatusKind.NeedsPassphrase)]
    [InlineData(SyncOutcome.AuthExpired, SyncStatusKind.SignInAgain)]
    [InlineData(SyncOutcome.Offline, SyncStatusKind.Offline)]
    [InlineData(SyncOutcome.RemoteTooNew, SyncStatusKind.UpdateApp)]
    [InlineData(SyncOutcome.RemoteUnreadable, SyncStatusKind.Unreadable)]
    [InlineData(SyncOutcome.Failed, SyncStatusKind.Failed)]
    public async Task Run_MapsOutcomeToStatus(SyncOutcome outcome, SyncStatusKind expected)
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(new SyncResult(outcome));

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(expected, coordinator.Status.Kind);
        Assert.Equal("Test cloud", coordinator.Status.ProviderName);
    }

    [Fact]
    public async Task Run_ConflictOutcome_ShowsConflictsWithCount()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(ConflictResult(Conflict("a"), Conflict("b")));
        _resolver.Answers.Enqueue(null);

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
        Assert.Equal(2, coordinator.Status.ConflictCount);
        Assert.Equal(2, coordinator.PendingConflicts.Count);
    }

    [Fact]
    public async Task Run_ShowsSyncingWhileRunning()
    {
        var coordinator = await CreateLinkedAsync();
        var seen = new List<SyncStatusKind>();
        coordinator.StatusChanged += (_, _) => seen.Add(coordinator.Status.Kind);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal([SyncStatusKind.Syncing, SyncStatusKind.Idle], seen);
    }

    // ---- Refresh after applied data ----

    [Theory]
    [InlineData(SyncOutcome.Synced)]
    [InlineData(SyncOutcome.Offline)]
    [InlineData(SyncOutcome.Failed)]
    public async Task Run_ThatChangedLocalData_RefreshesOnceWhateverTheOutcome(SyncOutcome outcome)
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(new SyncResult(outcome) { LocalDataChanged = true });

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(1, _refresher.Calls);
    }

    [Fact]
    public async Task Run_WithoutLocalChanges_DoesNotRefresh()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(0, _refresher.Calls);
    }

    [Fact]
    public async Task Run_Cancelled_RefreshesBecauseDataMayHaveChanged()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Throw = new OperationCanceledException();

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(1, _refresher.Calls);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);   // restored from the stored "Synced"
    }

    [Fact]
    public async Task Run_CancelledWithKnownConflicts_KeepsTheCountConsistent()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(ConflictResult(Conflict("a")));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);
        _sync.Throw = new OperationCanceledException();

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
        Assert.Equal(1, coordinator.Status.ConflictCount);
        Assert.Single(coordinator.PendingConflicts);
    }

    [Fact]
    public async Task Init_ClearsPendingConflicts()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(ConflictResult(Conflict("a")));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        await coordinator.InitializeAsync();

        Assert.Empty(coordinator.PendingConflicts);
        Assert.Equal(0, coordinator.Status.ConflictCount);
    }

    [Fact]
    public async Task ApplyTrigger_IsIgnored()
    {
        var coordinator = await CreateLinkedAsync();

        await coordinator.SyncNowAsync(SyncTrigger.Apply);

        Assert.Empty(_sync.SyncCalls);
    }

    // ---- Unexpected exceptions ----

    [Fact]
    public async Task RunThatThrows_DoesNotFaultCoalescedCallers_ShowsFailed_AndRunsTheFollowUp()
    {
        var coordinator = await CreateLinkedAsync();
        var seen = new List<SyncStatusKind>();
        coordinator.StatusChanged += (_, _) => seen.Add(coordinator.Status.Kind);
        var release = _sync.BlockNext();

        var manual = coordinator.SyncNowAsync(SyncTrigger.Manual);
        await _sync.Entered.Task;
        var timer = coordinator.SyncNowAsync(SyncTrigger.Timer);
        _state.FailNextLoad = true;   // the state read after the operation throws
        release.SetResult(new SyncResult(SyncOutcome.Synced));
        await manual;
        await timer;

        Assert.True(timer.IsCompletedSuccessfully);
        Assert.Contains(SyncStatusKind.Failed, seen);
        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    [Fact]
    public async Task ThrowingStatusSubscriber_DoesNotBreakRunsOrCoalescedCallers()
    {
        var coordinator = await CreateLinkedAsync();
        var otherCalls = 0;
        coordinator.StatusChanged += (_, _) => throw new InvalidOperationException("broken subscriber");
        coordinator.StatusChanged += (_, _) => otherCalls++;
        var release = _sync.BlockNext();

        var manual = coordinator.SyncNowAsync(SyncTrigger.Manual);
        await _sync.Entered.Task;
        var timer = coordinator.SyncNowAsync(SyncTrigger.Timer);
        release.SetResult(new SyncResult(SyncOutcome.Synced));
        await manual;
        await timer;

        Assert.True(timer.IsCompletedSuccessfully);
        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.True(otherCalls > 0);
    }

    [Fact]
    public async Task ThrowingResolver_ShowsFailedWithoutThrowing()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(ConflictResult(Conflict("a")));
        _resolver.Throw = new InvalidOperationException("broken list");

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(SyncStatusKind.Failed, coordinator.Status.Kind);
        Assert.Empty(coordinator.PendingConflicts);

        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));
        await coordinator.SyncNowAsync(SyncTrigger.Manual);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    // ---- Dispatcher ----

    [Fact]
    public async Task SeamCallsAndStatusChanged_GoThroughTheDispatcher()
    {
        var coordinator = await CreateLinkedAsync();
        var statusOutside = 0;
        coordinator.StatusChanged += (_, _) => { if (!_dispatcher.IsInside) statusOutside++; };
        _sync.Results.Enqueue(ConflictResult(Conflict("a")) with { LocalDataChanged = true });
        _resolver.Answers.Enqueue([Conflict("a").Resolve(ConflictChoice.Local)]);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced) { LocalDataChanged = true });

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(0, statusOutside);
        Assert.Single(_resolver.Calls);
        Assert.Equal(0, _resolver.CallsOutsideDispatcher);
        Assert.Equal(2, _refresher.Calls);
        Assert.Equal(0, _refresher.CallsOutsideDispatcher);
    }

    // ---- Gate and the unlinked follow-up ----

    [Fact]
    public async Task AutomaticTriggers_WhenNotLinked_AreIgnored()
    {
        var coordinator = Create();
        await coordinator.InitializeAsync();

        foreach (var trigger in new[] { SyncTrigger.Resume, SyncTrigger.LocalChange, SyncTrigger.Timer, SyncTrigger.Connectivity })
            await coordinator.SyncNowAsync(trigger);

        Assert.Empty(_sync.SyncCalls);
        Assert.Equal(1, _state.Loads);   // only the init read
    }

    [Theory]
    [InlineData(SyncOutcome.NeedsPassphrase, false)]
    [InlineData(SyncOutcome.AuthExpired, false)]
    [InlineData(SyncOutcome.RemoteTooNew, false)]
    [InlineData(SyncOutcome.RemoteUnreadable, false)]
    [InlineData(SyncOutcome.Offline, true)]
    [InlineData(SyncOutcome.Failed, true)]
    public async Task AutomaticTriggers_StopOnlyForNeedsYouStatuses(SyncOutcome outcome, bool keepsRunning)
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(new SyncResult(outcome));
        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        await coordinator.SyncNowAsync(SyncTrigger.Timer);

        Assert.Equal(keepsRunning ? 2 : 1, _sync.SyncCalls.Count);
    }

    [Fact]
    public async Task FollowUpQueuedBeforeForgettingTheLink_DoesNothing()
    {
        var coordinator = await CreateLinkedAsync();
        var release = _sync.BlockNext();

        var first = coordinator.SyncNowAsync(SyncTrigger.Manual);
        await _sync.Entered.Task;
        var followUp = coordinator.SyncNowAsync(SyncTrigger.LocalChange);
        var forget = coordinator.ForgetLinkForEraseAsync();
        release.SetResult(new SyncResult(SyncOutcome.Synced));
        await Task.WhenAll(first, followUp, forget);

        Assert.Single(_sync.SyncCalls);
        Assert.Equal(1, _sync.ForgetCalls);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
    }

    // ---- Coalescing ----

    [Fact]
    public async Task TriggersDuringARun_CoalesceIntoOneFollowUp()
    {
        var coordinator = await CreateLinkedAsync();
        var release = _sync.BlockNext();

        var first = coordinator.SyncNowAsync(SyncTrigger.Manual);
        await _sync.Entered.Task;
        var second = coordinator.SyncNowAsync(SyncTrigger.LocalChange);
        var third = coordinator.SyncNowAsync(SyncTrigger.Timer);
        release.SetResult(new SyncResult(SyncOutcome.Synced));
        await Task.WhenAll(first, second, third);

        Assert.Equal(2, _sync.SyncCalls.Count);
    }

    // ---- Conflicts ----

    [Fact]
    public async Task Conflicts_OpenTheResolverWithThem()
    {
        var coordinator = await CreateLinkedAsync();
        var conflict = Conflict("a");
        _sync.Results.Enqueue(ConflictResult(conflict));
        _resolver.Answers.Enqueue([conflict.Resolve(ConflictChoice.Remote)]);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));

        await coordinator.SyncNowAsync(SyncTrigger.Timer);

        Assert.Equal([conflict], Assert.Single(_resolver.Calls));
        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Null(_sync.SyncCalls[0]);
        Assert.Equal([conflict.Resolve(ConflictChoice.Remote)], _sync.SyncCalls[1]);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Empty(coordinator.PendingConflicts);
    }

    [Fact]
    public async Task Later_KeepsConflicts_AutomaticTriggersWait_ManualReruns()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(ConflictResult(Conflict("a")));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        await coordinator.SyncNowAsync(SyncTrigger.LocalChange);

        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
        Assert.Single(_sync.SyncCalls);

        _sync.Results.Enqueue(ConflictResult(Conflict("a")));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Equal(2, _resolver.Calls.Count);
        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
    }

    [Fact]
    public async Task ApplyThatFindsNewConflicts_ShowsTheListAgainBeforeApplyingMore()
    {
        var coordinator = await CreateLinkedAsync();
        var first = Conflict("a1");
        var second = Conflict("a2");
        var firstAnswer = new[] { first.Resolve(ConflictChoice.Local) };
        var secondAnswer = new[] { second.Resolve(ConflictChoice.Remote) };
        _sync.Results.Enqueue(ConflictResult(first));
        _resolver.Answers.Enqueue(firstAnswer);
        _sync.Results.Enqueue(ConflictResult(second));
        _resolver.Answers.Enqueue(secondAnswer);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));

        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(2, _resolver.Calls.Count);
        Assert.Equal([first], _resolver.Calls[0]);
        Assert.Equal([second], _resolver.Calls[1]);
        // Each resolver showing saw exactly the sync calls before it: nothing was applied without the user.
        Assert.Equal([1, 2], _resolver.SyncCallsWhenShown);
        Assert.Equal(3, _sync.SyncCalls.Count);
        Assert.Equal(firstAnswer, _sync.SyncCalls[1]);
        Assert.Equal(secondAnswer, _sync.SyncCalls[2]);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    [Fact]
    public async Task Start_WithConflictsFromThePreviousSession_RerunsAndOpensTheList()
    {
        var coordinator = await CreateLinkedAsync("NeedsConflictResolution");
        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
        Assert.Equal(0, coordinator.Status.ConflictCount);

        await coordinator.SyncNowAsync(SyncTrigger.Resume);
        Assert.Empty(_sync.SyncCalls);

        var conflict = Conflict("a");
        _sync.Results.Enqueue(ConflictResult(conflict));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Start);

        Assert.Single(_sync.SyncCalls);
        Assert.Null(_sync.SyncCalls[0]);
        Assert.Equal([conflict], Assert.Single(_resolver.Calls));
        Assert.Equal(1, coordinator.Status.ConflictCount);
    }

    [Fact]
    public async Task OpenConflicts_ShowsPendingConflicts()
    {
        var coordinator = await CreateLinkedAsync();
        var conflict = Conflict("a");
        _sync.Results.Enqueue(ConflictResult(conflict));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);
        _resolver.Answers.Enqueue([conflict.Resolve(ConflictChoice.Local)]);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));

        await coordinator.OpenConflictsAsync();

        Assert.Equal(2, _resolver.Calls.Count);
        Assert.Equal([conflict.Resolve(ConflictChoice.Local)], _sync.SyncCalls[1]);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    [Fact]
    public async Task ConflictsFoundWhileTheListIsOpen_UpdatePendingWithoutASecondList()
    {
        var coordinator = await CreateLinkedAsync();
        var first = Conflict("a1");
        var second = Conflict("a2");
        _sync.Results.Enqueue(ConflictResult(first));
        _resolver.Answers.Enqueue(null);
        await coordinator.SyncNowAsync(SyncTrigger.Manual);
        var open = _resolver.BlockNext();

        var list = coordinator.OpenConflictsAsync();
        await _resolver.Entered.Task;
        _sync.Results.Enqueue(ConflictResult(second));
        await coordinator.SyncNowAsync(SyncTrigger.Manual);

        Assert.Equal(2, _resolver.Calls.Count);
        Assert.Equal([second], coordinator.PendingConflicts);
        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);

        open.SetResult(null);
        await list;
        Assert.Equal(2, _resolver.Calls.Count);
        Assert.Equal(2, _sync.SyncCalls.Count);
    }

    // ---- Connect ----

    [Fact]
    public async Task Connect_EmptyCloud_AsksForANewPassphrase_StoresIt_AndSyncsAlthoughNotLinked()
    {
        var coordinator = Create();
        await coordinator.InitializeAsync();
        _catalog.Provider.Info = null;
        _prompt.Results.Enqueue(new PassphrasePromptResult("new secret", false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([(PassphrasePromptKind.CreateNew, false)], _prompt.Calls);
        Assert.Equal("new secret", _secrets.Value);
        Assert.Equal(["sync"], _sync.Log);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Equal(0, _prompt.CallsOutsideDispatcher);
    }

    [Fact]
    public async Task Connect_ExistingFile_AsksForItsPassphrase_AndUnlocksBeforeSyncing()
    {
        var coordinator = Create();
        _prompt.Results.Enqueue(new PassphrasePromptResult("right", false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([(PassphrasePromptKind.EnterExisting, false)], _prompt.Calls);
        Assert.Equal(["unlock:right", "sync"], _sync.Log);
        Assert.Equal("right", _secrets.Value);
    }

    [Fact]
    public async Task Connect_WrongPassphrase_AsksAgainWithTheFailureShown()
    {
        var coordinator = Create();
        _sync.UnlockResults.Enqueue(new SyncResult(SyncOutcome.WrongPassphrase));
        _prompt.Results.Enqueue(new PassphrasePromptResult("typo", false));
        _prompt.Results.Enqueue(new PassphrasePromptResult("right", false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([(PassphrasePromptKind.EnterExisting, false), (PassphrasePromptKind.EnterExisting, true)], _prompt.Calls);
        Assert.Equal(["unlock:typo", "unlock:right", "sync"], _sync.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connect_Cancelled_ClearsTheStoredSecret_AndDoesNotSync(bool cloudFileExists)
    {
        var coordinator = Create();
        if (!cloudFileExists)
            _catalog.Provider.Info = null;
        _secrets.Value = "left over";
        _prompt.Results.Enqueue(new PassphrasePromptResult(null, false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Null(_secrets.Value);
        Assert.Empty(_sync.Log);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
    }

    [Fact]
    public async Task Connect_ForgotIt_ConfirmDeclined_ChangesNothing()
    {
        var coordinator = Create();
        _secrets.Value = "kept";
        _prompt.Results.Enqueue(new PassphrasePromptResult(null, true));
        _shell.Answers.Enqueue(false);

        await coordinator.ConnectAsync(ProviderId);

        Assert.True(Assert.Single(_shell.Calls).Destructive);
        Assert.Single(_prompt.Calls);
        Assert.Empty(_sync.Log);
        Assert.Equal("kept", _secrets.Value);
        Assert.Equal(0, _secrets.Sets);
    }

    [Fact]
    public async Task Connect_ForgotIt_Confirmed_ReplacesTheCloudUnderANewPassphrase()
    {
        var coordinator = Create();
        _prompt.Results.Enqueue(new PassphrasePromptResult(null, true));
        _prompt.Results.Enqueue(new PassphrasePromptResult("fresh", false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([(PassphrasePromptKind.EnterExisting, false), (PassphrasePromptKind.CreateNew, false)], _prompt.Calls);
        var confirm = Assert.Single(_shell.Calls);
        Assert.True(confirm.Destructive);
        Assert.Equal(Strings.Sync_ReplaceConfirmMessage, confirm.Message);
        Assert.Equal(["replace:fresh"], _sync.Log);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.True(coordinator.IsLinked);
    }

    [Fact]
    public async Task Connect_CloudUnreachable_ShowsFeedbackWithoutThrowing()
    {
        var coordinator = Create();
        _catalog.Provider.InfoError = new SyncOfflineException();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(Strings.Sync_ConnectFailed, Assert.Single(_feedback.Titles));
        Assert.Equal(Strings.Sync_FailOffline, Assert.Single(_feedback.Details));
        Assert.Empty(_prompt.Calls);
        Assert.Empty(_sync.Log);
    }

    [Fact]
    public async Task Connect_UnlockFailsForAnotherReason_ShowsTheCause_AndAbandons()
    {
        var coordinator = Create();
        _sync.UnlockResults.Enqueue(new SyncResult(SyncOutcome.RemoteUnreadable));
        _prompt.Results.Enqueue(new PassphrasePromptResult("right", false));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(Strings.Sync_ConnectFailed, Assert.Single(_feedback.Titles));
        Assert.Equal(Strings.Sync_StatusUnreadable, Assert.Single(_feedback.Details));
        Assert.Equal(["unlock:right"], _sync.Log);
        Assert.Null(_secrets.Value);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
    }

    public static TheoryData<SyncOutcome, string> FailedFirstRuns => new()
    {
        { SyncOutcome.Offline, Strings.Sync_FailOffline },
        { SyncOutcome.RemoteUnreadable, Strings.Sync_StatusUnreadable },
        { SyncOutcome.RemoteTooNew, Strings.Sync_StatusUpdateApp },
        { SyncOutcome.AuthExpired, Strings.Sync_StatusSignInAgain },
        { SyncOutcome.WrongPassphrase, Strings.Sync_FailWrongPassphrase },
        { SyncOutcome.Failed, Strings.Backup_UnexpectedError },
    };

    [Theory]
    [MemberData(nameof(FailedFirstRuns))]
    public async Task Connect_FirstRunThatNeitherLinksNorStopsAtConflicts_AbandonsTheConnect(SyncOutcome outcome, string detail)
    {
        var coordinator = Create();
        _catalog.Provider.Info = null;
        _prompt.Results.Enqueue(new PassphrasePromptResult("new secret", false));
        _sync.Results.Enqueue(new SyncResult(outcome));

        await coordinator.ConnectAsync(ProviderId);

        Assert.Null(_secrets.Value);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
        Assert.Equal(Strings.Sync_ConnectFailed, Assert.Single(_feedback.Titles));
        Assert.Equal(detail, Assert.Single(_feedback.Details));

        // Nothing keeps running for the abandoned connect.
        await coordinator.SyncNowAsync(SyncTrigger.Manual);
        Assert.Single(_sync.SyncCalls);
    }

    /// <summary>A device that disconnected with a kept base reconnects, and both sides changed the same record.</summary>
    private void DisconnectedWithKeptBase()
    {
        Link();
        _state.Value.IsConnected = false;
    }

    [Fact]
    public async Task Connect_FirstRunWithConflicts_AppliesTheAnswersAndLinks()
    {
        DisconnectedWithKeptBase();
        var coordinator = Create();
        await coordinator.InitializeAsync();
        var conflict = Conflict("a");
        ConflictResolution[] answers = [new(conflict.SyncId, ConflictChoice.Local, "a")];
        _prompt.Results.Enqueue(new PassphrasePromptResult("right", false));
        _sync.Results.Enqueue(ConflictResult(conflict));
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));
        _resolver.Answers.Enqueue(answers);

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Null(_sync.SyncCalls[0]);
        Assert.Equal(answers, _sync.SyncCalls[1]);
        Assert.True(coordinator.IsLinked);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Equal("right", _secrets.Value);
    }

    [Fact]
    public async Task Connect_FirstRunWithConflicts_Later_ThenResolve_Applies()
    {
        DisconnectedWithKeptBase();
        var coordinator = Create();
        await coordinator.InitializeAsync();
        var conflict = Conflict("a");
        ConflictResolution[] answers = [new(conflict.SyncId, ConflictChoice.Remote, "a")];
        _prompt.Results.Enqueue(new PassphrasePromptResult("right", false));
        _sync.Results.Enqueue(ConflictResult(conflict));
        _resolver.Answers.Enqueue(null);

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(SyncStatusKind.Conflicts, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
        Assert.Equal("right", _secrets.Value);
        Assert.Empty(_feedback.Titles);

        _sync.Results.Enqueue(new SyncResult(SyncOutcome.Synced));
        _resolver.Answers.Enqueue(answers);
        await coordinator.OpenConflictsAsync();

        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Equal(answers, _sync.SyncCalls[1]);
        Assert.True(coordinator.IsLinked);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    // ---- Passphrase ----

    [Fact]
    public async Task EnterPassphrase_UnlocksWithRetries_ThenSyncs()
    {
        var coordinator = await CreateLinkedAsync(lastAttempt: "WrongPassphrase");
        _sync.UnlockResults.Enqueue(new SyncResult(SyncOutcome.WrongPassphrase));
        _prompt.Results.Enqueue(new PassphrasePromptResult("old", false));
        _prompt.Results.Enqueue(new PassphrasePromptResult("changed", false));

        await coordinator.EnterPassphraseAsync();

        Assert.Equal([(PassphrasePromptKind.EnterChanged, false), (PassphrasePromptKind.EnterChanged, true)], _prompt.Calls);
        Assert.Equal(["unlock:old", "unlock:changed", "sync"], _sync.Log);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    [Fact]
    public async Task EnterPassphrase_Cancelled_DoesNothing()
    {
        var coordinator = await CreateLinkedAsync(lastAttempt: "NeedsPassphrase");
        _prompt.Results.Enqueue(new PassphrasePromptResult(null, false));

        await coordinator.EnterPassphraseAsync();

        Assert.Empty(_sync.Log);
        Assert.Equal(SyncStatusKind.NeedsPassphrase, coordinator.Status.Kind);
    }

    [Fact]
    public async Task ChangePassphrase_ReencryptsAndConfirms()
    {
        var coordinator = await CreateLinkedAsync();
        _prompt.Results.Enqueue(new PassphrasePromptResult("new one", false));

        await coordinator.ChangePassphraseAsync();

        Assert.Equal([(PassphrasePromptKind.ChangeOwn, false)], _prompt.Calls);
        Assert.Equal(["change:new one"], _sync.Log);
        Assert.Equal(Strings.Sync_PassphraseChanged, Assert.Single(_feedback.Titles));
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
    }

    [Fact]
    public async Task ChangePassphrase_NotStoredOnThisDevice_AsksForItWithTheHint()
    {
        var coordinator = await CreateLinkedAsync();
        _sync.ChangeResult = new SyncResult(SyncOutcome.Failed, Detail: SyncResult.PassphraseNotStoredDetail);
        _prompt.Results.Enqueue(new PassphrasePromptResult("new one", false));

        await coordinator.ChangePassphraseAsync();

        Assert.Equal(SyncStatusKind.NeedsPassphrase, coordinator.Status.Kind);
        Assert.Equal(Strings.Sync_PassphraseNotStored, Assert.Single(_feedback.Details));
    }

    [Fact]
    public async Task ChangePassphrase_Cancelled_DoesNothing()
    {
        var coordinator = await CreateLinkedAsync();
        _prompt.Results.Enqueue(new PassphrasePromptResult(null, false));

        await coordinator.ChangePassphraseAsync();

        Assert.Empty(_sync.Log);
        Assert.Empty(_feedback.Titles);
    }

    // ---- Replace ----

    [Fact]
    public async Task ReplaceCloud_Declined_DoesNotAskForAPassphrase()
    {
        var coordinator = await CreateLinkedAsync(lastAttempt: "RemoteUnreadable");
        _shell.Answers.Enqueue(false);

        await coordinator.ReplaceCloudAsync();

        var confirm = Assert.Single(_shell.Calls);
        Assert.True(confirm.Destructive);
        Assert.Equal(Strings.Sync_ReplaceConfirmMessage, confirm.Message);
        Assert.Empty(_prompt.Calls);
        Assert.Empty(_sync.Log);
    }

    [Fact]
    public async Task ReplaceCloud_Confirmed_ReplacesUnderANewPassphrase()
    {
        var coordinator = await CreateLinkedAsync(lastAttempt: "RemoteUnreadable");
        _prompt.Results.Enqueue(new PassphrasePromptResult("fresh", false));

        await coordinator.ReplaceCloudAsync();

        Assert.True(Assert.Single(_shell.Calls).Destructive);
        Assert.Equal([(PassphrasePromptKind.CreateNew, false)], _prompt.Calls);
        Assert.Equal(["replace:fresh"], _sync.Log);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Equal(0, _shell.CallsOutsideDispatcher);
    }

    // ---- Disconnect ----

    [Fact]
    public async Task Disconnect_Declined_DoesNothing()
    {
        var coordinator = await CreateLinkedAsync();
        _shell.Answers.Enqueue(false);

        await coordinator.DisconnectAsync();

        Assert.Single(_shell.Calls);
        Assert.Empty(_sync.Log);
        Assert.True(coordinator.IsLinked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disconnect_AsksSeparatelyWhetherToDeleteTheCloudFile(bool deleteRemote)
    {
        var coordinator = await CreateLinkedAsync();
        _shell.Answers.Enqueue(true);
        _shell.Answers.Enqueue(deleteRemote);

        await coordinator.DisconnectAsync();

        Assert.Equal(2, _shell.Calls.Count);
        Assert.False(_shell.Calls[0].Destructive);
        Assert.True(_shell.Calls[1].Destructive);
        Assert.Equal([$"disconnect:{deleteRemote}"], _sync.Log);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(coordinator.IsLinked);
    }

    // ---- Erase support ----

    [Fact]
    public async Task HasSyncState_IsTrueForADisconnectedDeviceWithAKeptBase()
    {
        Link();
        _state.Value.IsConnected = false;
        var coordinator = Create();

        Assert.True(await coordinator.HasSyncStateAsync());
        Assert.False(coordinator.IsLinked);
    }

    [Fact]
    public async Task HasSyncState_IsFalseWithoutARow()
    {
        Assert.False(await Create().HasSyncStateAsync());
    }

    [Theory]
    [InlineData(SyncOutcome.Synced, true)]
    [InlineData(SyncOutcome.NoChanges, true)]
    [InlineData(SyncOutcome.Offline, false)]
    [InlineData(SyncOutcome.NeedsConflictResolution, false)]
    public async Task TrySyncNow_TellsWhetherTheDeviceIsSynced(SyncOutcome outcome, bool expected)
    {
        var coordinator = await CreateLinkedAsync();
        _sync.Results.Enqueue(outcome == SyncOutcome.NeedsConflictResolution
            ? ConflictResult(Conflict("a"))
            : new SyncResult(outcome));

        Assert.Equal(expected, await coordinator.TrySyncNowAsync());
    }

    // ---- Fakes ----

    private sealed class FakeDispatcher : IUiDispatcher
    {
        private readonly AsyncLocal<bool> _inside = new();

        public bool IsInside => _inside.Value;

        public async Task InvokeAsync(Func<Task> action)
        {
            _inside.Value = true;
            try
            {
                await action();
            }
            finally
            {
                _inside.Value = false;
            }
        }
    }

    private sealed class FakeSession(ISyncService sync, ISyncStateStore state) : ISyncSession
    {
        public ISyncService Sync => sync;

        public ISyncStateStore State => state;
    }

    private sealed class FakeStateStore : ISyncStateStore
    {
        public SyncState Value { get; set; } = new();
        public int Loads { get; private set; }
        public bool FailNextLoad { get; set; }

        public Task<SyncState> LoadAsync()
        {
            Loads++;
            if (FailNextLoad)
            {
                FailNextLoad = false;
                throw new IOException("database locked");
            }

            return Task.FromResult(Value);
        }

        public Task SaveAsync(SyncState state)
        {
            Value = state;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            Value = new SyncState();
            return Task.CompletedTask;
        }

        public Task<bool> HasSyncStateAsync() => Task.FromResult(Value.ProviderId is not null);
    }

    private sealed class FakeSyncService(FakeStateStore state, FakeSecrets secrets) : ISyncService
    {
        private TaskCompletionSource<SyncResult>? _block;

        /// <summary>Every core call except forget, in order.</summary>
        public List<string> Log { get; } = [];
        public Queue<SyncResult> UnlockResults { get; } = new();
        public SyncResult ChangeResult { get; set; } = new(SyncOutcome.Synced);
        public Queue<SyncResult> Results { get; } = new();
        public List<IReadOnlyCollection<ConflictResolution>?> SyncCalls { get; } = [];
        public int ForgetCalls { get; private set; }
        public Exception? Throw { get; set; }
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<SyncResult> BlockNext()
        {
            Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _block = new TaskCompletionSource<SyncResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyCollection<ConflictResolution>? resolutions = null, CancellationToken ct = default)
        {
            SyncCalls.Add(resolutions);
            Log.Add("sync");
            Entered.TrySetResult();
            if (Throw is not null)
                throw Throw;
            if (_block is { } block)
            {
                _block = null;
                return await block.Task;
            }

            var result = Results.TryDequeue(out var r) ? r : new SyncResult(SyncOutcome.NoChanges);
            state.Value.LastAttemptResult = result.Outcome.ToString();
            // Like the real one: a successful run links the device.
            if (result.Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges)
            {
                state.Value.ProviderId = provider.Id;
                state.Value.IsConnected = true;
            }

            return result;
        }

        public Task<SyncResult> ForgetLinkAsync(CancellationToken ct = default)
        {
            ForgetCalls++;
            state.Value = new SyncState();
            return Task.FromResult(new SyncResult(SyncOutcome.Disconnected));
        }

        public async Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default)
        {
            Log.Add($"replace:{newPassphrase}");
            await secrets.SetPassphraseAsync(newPassphrase);
            state.Value.ProviderId = provider.Id;
            state.Value.IsConnected = true;
            state.Value.LastAttemptResult = nameof(SyncOutcome.Synced);
            return new SyncResult(SyncOutcome.Synced);
        }

        public Task<SyncResult> ChangePassphraseAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default)
        {
            Log.Add($"change:{newPassphrase}");
            return Task.FromResult(ChangeResult);
        }

        public Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default)
        {
            Log.Add($"disconnect:{deleteRemote}");
            if (deleteRemote)
                state.Value = new SyncState();
            else
                state.Value.IsConnected = false;
            return Task.FromResult(new SyncResult(SyncOutcome.Disconnected));
        }

        /// <summary>Like the real one: stores the passphrase only when it opens the cloud file.</summary>
        public async Task<SyncResult> UnlockAsync(ICloudSyncProvider provider, string passphrase, CancellationToken ct = default)
        {
            Log.Add($"unlock:{passphrase}");
            var result = UnlockResults.TryDequeue(out var r) ? r : new SyncResult(SyncOutcome.NoChanges);
            if (result.Outcome == SyncOutcome.NoChanges)
                await secrets.SetPassphraseAsync(passphrase);
            return result;
        }
    }

    private sealed class FakeResolver : IConflictResolutionService
    {
        private TaskCompletionSource<IReadOnlyList<ConflictResolution>?>? _block;

        public Exception? Throw { get; set; }

        public FakeDispatcher? Dispatcher { get; set; }
        public Func<int>? SyncCallCount { get; set; }
        public Queue<IReadOnlyList<ConflictResolution>?> Answers { get; } = new();
        public List<IReadOnlyList<SyncConflict>> Calls { get; } = [];
        public List<int> SyncCallsWhenShown { get; } = [];
        public int CallsOutsideDispatcher { get; private set; }
        public TaskCompletionSource Entered { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<IReadOnlyList<ConflictResolution>?> BlockNext()
        {
            Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _block = new TaskCompletionSource<IReadOnlyList<ConflictResolution>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<IReadOnlyList<ConflictResolution>?> ResolveAsync(IReadOnlyList<SyncConflict> conflicts)
        {
            Calls.Add(conflicts);
            if (SyncCallCount is not null)
                SyncCallsWhenShown.Add(SyncCallCount());
            if (Dispatcher?.IsInside != true)
                CallsOutsideDispatcher++;
            Entered.TrySetResult();
            if (Throw is { } ex)
            {
                Throw = null;
                throw ex;
            }

            if (_block is { } block)
            {
                _block = null;
                return block.Task;
            }

            return Task.FromResult(Answers.TryDequeue(out var a) ? a : null);
        }
    }

    private sealed class FakeRefresher : ISyncDataRefresher
    {
        public FakeDispatcher? Dispatcher { get; set; }
        public int Calls { get; private set; }
        public int CallsOutsideDispatcher { get; private set; }

        public Task RefreshAfterSyncAsync()
        {
            Calls++;
            if (Dispatcher?.IsInside != true)
                CallsOutsideDispatcher++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalog : ICloudProviderCatalog
    {
        public FakeProvider Provider { get; } = new();

        public IReadOnlyList<CloudProviderOption> Available { get; } = [new(ProviderId, "Test cloud")];

        public ICloudSyncProvider? Get(string providerId) => providerId == ProviderId ? Provider : null;
    }

    private sealed class FakeProvider : ICloudSyncProvider
    {
        public RemoteInfo? Info { get; set; } = new("rev-1", LastSync);
        public Exception? InfoError { get; set; }
        public string Id => ProviderId;
        public string? AccountName => "me@example.com";

        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) =>
            InfoError is { } error ? Task.FromException<RemoteInfo?>(error) : Task.FromResult(Info);

        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSecrets : ISyncSecretStore
    {
        public string? Value { get; set; }
        public int Sets { get; private set; }

        public Task<string?> GetPassphraseAsync() => Task.FromResult(Value);

        public Task SetPassphraseAsync(string? passphrase)
        {
            Sets++;
            Value = passphrase;
            return Task.CompletedTask;
        }
    }

    private sealed class FakePassphrasePrompt : ISyncPassphrasePromptService
    {
        public FakeDispatcher? Dispatcher { get; set; }
        public Queue<PassphrasePromptResult> Results { get; } = new();
        public List<(PassphrasePromptKind Kind, bool PreviousAttemptFailed)> Calls { get; } = [];
        public int CallsOutsideDispatcher { get; private set; }

        public Task<PassphrasePromptResult> PromptAsync(PassphrasePromptKind kind, bool previousAttemptFailed)
        {
            Calls.Add((kind, previousAttemptFailed));
            if (Dispatcher?.IsInside != true)
                CallsOutsideDispatcher++;
            return Task.FromResult(Results.TryDequeue(out var r) ? r : new PassphrasePromptResult(null, false));
        }
    }

    private sealed class FakeShell : IAppShellService
    {
        public FakeDispatcher? Dispatcher { get; set; }
        public Queue<bool> Answers { get; } = new();
        public List<(string Title, string Message, bool Destructive)> Calls { get; } = [];
        public int CallsOutsideDispatcher { get; private set; }
        public DevicePlatform Platform => DevicePlatform.Android;

        public Task GoToAsync(string route) => Task.CompletedTask;

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false)
        {
            Calls.Add((title, message, destructive));
            if (Dispatcher?.IsInside != true)
                CallsOutsideDispatcher++;
            return Task.FromResult(!Answers.TryDequeue(out var answer) || answer);
        }

        public Task ShowMessageAsync(string title, string message, string close) => Task.CompletedTask;
    }

    private sealed class FakeFeedback : IFeedbackService
    {
        public List<string> Titles { get; } = [];
        public List<string?> Details { get; } = [];

        public Task ShowAsync(string title, string? detail = null)
        {
            Titles.Add(title);
            Details.Add(detail);
            return Task.CompletedTask;
        }

        public Task<bool> ShowUndoAsync(string message) => Task.FromResult(false);
    }
}
