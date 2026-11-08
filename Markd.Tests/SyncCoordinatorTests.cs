using Markd.Core.Domain;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Microsoft.Extensions.Logging.Abstractions;
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

    public SyncCoordinatorTests() => _sync = new FakeSyncService(_state);

    private SyncCoordinator Create()
    {
        _resolver.Dispatcher = _dispatcher;
        _refresher.Dispatcher = _dispatcher;
        _resolver.SyncCallCount = () => _sync.SyncCalls.Count;
        return new SyncCoordinator(new FakeSession(_sync, _state), _catalog, _resolver, _refresher, _dispatcher,
            NullLogger<SyncCoordinator>.Instance);
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

    private sealed class FakeSyncService(FakeStateStore state) : ISyncService
    {
        private TaskCompletionSource<SyncResult>? _block;

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
            return result;
        }

        public Task<SyncResult> ForgetLinkAsync(CancellationToken ct = default)
        {
            ForgetCalls++;
            state.Value = new SyncState();
            return Task.FromResult(new SyncResult(SyncOutcome.Disconnected));
        }

        public Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SyncResult> ChangePassphraseAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SyncResult> UnlockAsync(ICloudSyncProvider provider, string passphrase, CancellationToken ct = default) =>
            throw new NotSupportedException();
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
        public IReadOnlyList<CloudProviderOption> Available { get; } = [new(ProviderId, "Test cloud")];

        public ICloudSyncProvider? Get(string providerId) => providerId == ProviderId ? new FakeProvider() : null;
    }

    private sealed class FakeProvider : ICloudSyncProvider
    {
        public string Id => ProviderId;
        public string? AccountName => "me@example.com";
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<string> UploadAsync(byte[] data, string? expectedRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
