using Markd.Core.Localization;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Markd.Tests.SyncCoordinatorTests;

namespace Markd.Tests;

/// <summary>Interactive sign-in from Connect and Sign in again, and signing out on disconnect.</summary>
public class SyncCoordinatorSignInTests
{
    private readonly FakeStateStore _state = new();
    private readonly FakeSyncService _sync;
    private readonly FakeDispatcher _dispatcher = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeSecrets _secrets = new();
    private readonly FakePassphrasePrompt _prompt = new();
    private readonly FakeSignInWait _wait = new();
    private readonly FakeShell _shell = new();
    private readonly FakeFeedback _feedback = new();
    private readonly FakeAuth _auth = new();
    private readonly List<string> _order = [];

    public SyncCoordinatorSignInTests()
    {
        _sync = new FakeSyncService(_state, _secrets);
        _catalog.Auth = _auth;
        _auth.Order = _order;
        _catalog.Provider.Order = _order;
        _sync.Order = _order;
    }

    private SyncCoordinator Create() =>
        new(new FakeSession(_sync, _state), _catalog, new FakeResolver(), new FakeRefresher(), _dispatcher,
            _secrets, _prompt, _wait, _shell, _feedback, NullLogger<SyncCoordinator>.Instance);

    private static Func<string?, CancellationToken, Task<CloudAccount?>> Returns(string id, string? email) =>
        (_, _) => Task.FromResult<CloudAccount?>(new CloudAccount(id, email));

    /// <summary>Linked to account "A"; signed out when <paramref name="signedIn"/> is false.</summary>
    private async Task<SyncCoordinator> CreateLinkedAsync(bool signedIn, string lastAttempt = "Synced")
    {
        _state.Value.ProviderId = ProviderId;
        _state.Value.AccountName = "A";
        _state.Value.AccountLabel = "a@x.com";
        _state.Value.IsConnected = true;
        _state.Value.LastSyncUtc = LastSync;
        _state.Value.LastResult = "Synced";
        _state.Value.LastAttemptResult = lastAttempt;
        _state.Value.BaseSnapshotJson = "{}";
        _secrets.Value = "pw-a";
        if (signedIn)
            _auth.Account = new CloudAccount("A", "a@x.com");
        var coordinator = Create();
        await coordinator.InitializeAsync();
        return coordinator;
    }

    private static PassphrasePromptResult Typed(string passphrase) => new(passphrase, false);

    // ---- Connect ----

    [Fact]
    public async Task Connect_WithoutSession_SignsInBeforeReadingTheCloud()
    {
        _auth.OnSignIn = Returns("A", "a@x.com");
        _prompt.Results.Enqueue(Typed("pw-a"));
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Single(_auth.SignInHints);
        Assert.Equal(["signin", "info"], _order);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.True(_state.Value.IsConnected);
        Assert.Equal("A", _state.Value.AccountName);
    }

    [Fact]
    public async Task Connect_WithSession_DoesNotSignIn()
    {
        _auth.Account = new CloudAccount("A", "a@x.com");
        _prompt.Results.Enqueue(Typed("pw-a"));
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Empty(_auth.SignInHints);
        Assert.True(_state.Value.IsConnected);
    }

    private void AssertQuietCancel(SyncCoordinator coordinator)
    {
        Assert.Empty(_feedback.Titles);
        Assert.Empty(_prompt.Calls);
        Assert.Null(_secrets.Value);
        Assert.False(_state.Value.IsConnected);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.Equal(0, _catalog.Provider.InfoCalls);
        Assert.Empty(_sync.Log);
    }

    [Fact]
    public async Task Connect_SignInCancelled_IsQuiet()
    {
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Single(_auth.SignInHints);
        AssertQuietCancel(coordinator);
    }

    [Fact]
    public async Task Connect_SignInTimesOut_IsQuiet()
    {
        _auth.OnSignIn = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        var coordinator = Create();
        coordinator.SignInTimeout = TimeSpan.FromMilliseconds(50);
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        AssertQuietCancel(coordinator);
    }

    [Fact]
    public async Task Connect_WaitPanelCancelled_IsQuiet()
    {
        _auth.SignInLeavesApp = true;
        _wait.Cancel = true;
        _auth.OnSignIn = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        AssertQuietCancel(coordinator);
    }

    [Fact]
    public async Task Connect_SignInFails_ShowsConnectFailed()
    {
        _auth.OnSignIn = (_, _) => throw new SyncOfflineException();
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([Strings.Sync_ConnectFailed], _feedback.Titles);
        Assert.Equal([Strings.Sync_SignInFailedDetail], _feedback.Details);
        Assert.Empty(_prompt.Calls);
        Assert.False(_state.Value.IsConnected);
        Assert.Equal(0, _catalog.Provider.InfoCalls);
    }

    [Fact]
    public async Task Connect_SignInMissingPermission_AsksToAllowIt()
    {
        _auth.OnSignIn = (_, _) => throw new SignInMissingPermissionException();
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal([Strings.Sync_ConnectFailed], _feedback.Titles);
        Assert.Equal([Strings.Sync_SignInMissingPermission], _feedback.Details);
        Assert.Empty(_prompt.Calls);
        Assert.False(_state.Value.IsConnected);
        Assert.Equal(0, _catalog.Provider.InfoCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connect_UsesWaitPanel_OnlyWhenSignInLeavesApp(bool leavesApp)
    {
        _auth.SignInLeavesApp = leavesApp;
        _auth.OnSignIn = Returns("A", "a@x.com");
        _prompt.Results.Enqueue(Typed("pw-a"));
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Single(_auth.SignInHints);
        Assert.Equal(leavesApp ? ["Test cloud"] : [], _wait.Calls);
        Assert.True(_state.Value.IsConnected);
    }

    [Fact]
    public async Task Connect_StoredSessionRevoked_SignsInAndRetries()
    {
        _auth.Account = new CloudAccount("A", "a@x.com");
        _auth.OnSignIn = Returns("A", "a@x.com");
        _catalog.Provider.NextInfoErrors.Enqueue(new SyncAuthException());
        _prompt.Results.Enqueue(Typed("pw-a"));
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(["info", "signin", "info"], _order);
        Assert.Empty(_feedback.Titles);
        Assert.True(_state.Value.IsConnected);
    }

    [Fact]
    public async Task Connect_StoredSessionRevoked_SecondAuthFailure_ShowsConnectFailed()
    {
        _auth.Account = new CloudAccount("A", "a@x.com");
        _auth.OnSignIn = Returns("A", "a@x.com");
        _catalog.Provider.NextInfoErrors.Enqueue(new SyncAuthException());
        _catalog.Provider.NextInfoErrors.Enqueue(new SyncAuthException());
        var coordinator = Create();
        await coordinator.InitializeAsync();

        await coordinator.ConnectAsync(ProviderId);

        Assert.Equal(["info", "signin", "info"], _order);
        Assert.Equal([Strings.Sync_ConnectFailed], _feedback.Titles);
        Assert.Empty(_prompt.Calls);
        Assert.False(_state.Value.IsConnected);
    }

    // ---- Automatic runs and restored databases ----

    [Fact]
    public async Task AutomaticTrigger_NeverSignsInInteractively()
    {
        var coordinator = await CreateLinkedAsync(signedIn: true);
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.AuthExpired));
        _sync.Results.Enqueue(new SyncResult(SyncOutcome.AuthExpired));

        await coordinator.SyncNowAsync(SyncTrigger.Timer);
        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
        await coordinator.SyncNowAsync(SyncTrigger.Start);

        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
        Assert.Equal(2, _sync.SyncCalls.Count);
        Assert.Empty(_auth.SignInHints);
    }

    [Fact]
    public async Task RestoredDatabase_WithoutTokens_ShowsSignInAgain()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false);

        await coordinator.SyncNowAsync(SyncTrigger.Start);

        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
        Assert.Equal("a@x.com", coordinator.Status.AccountLabel);
        Assert.Equal("{}", _state.Value.BaseSnapshotJson);
        Assert.Equal("A", _state.Value.AccountName);
        Assert.True(_state.Value.IsConnected);
        Assert.Empty(_auth.SignInHints);
    }

    [Fact]
    public async Task Initialize_LoadsAuthBeforeStatus()
    {
        _state.Value.ProviderId = ProviderId;
        _state.Value.AccountName = "A";
        _state.Value.IsConnected = true;
        _state.Value.LastAttemptResult = "Synced";
        var coordinator = Create();
        bool? loadedWhenShown = null;
        coordinator.StatusChanged += (_, _) => loadedWhenShown ??= _auth.Loaded;

        await coordinator.InitializeAsync();

        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.True(loadedWhenShown);
    }

    // ---- Sign in again ----

    [Fact]
    public async Task SignInAgain_SameAccount_ResumesSync()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");
        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
        _auth.OnSignIn = Returns("A", "new@x.com");

        await coordinator.SignInAgainAsync();

        Assert.Equal(["a@x.com"], _auth.SignInHints);
        Assert.Equal(["sync"], _sync.Log);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Equal("new@x.com", coordinator.Status.AccountLabel);
        Assert.Equal("pw-a", _secrets.Value);
    }

    [Fact]
    public async Task SignInAgain_Cancelled_KeepsTheLink()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");

        await coordinator.SignInAgainAsync();

        Assert.Single(_auth.SignInHints);
        Assert.Empty(_sync.Log);
        Assert.Empty(_feedback.Titles);
        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
        Assert.True(_state.Value.IsConnected);
        Assert.Equal("pw-a", _secrets.Value);
    }

    [Fact]
    public async Task SignInAgain_OtherAccount_GoesThroughConnect()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");
        _auth.OnSignIn = Returns("B", "b@x.com");
        _prompt.Results.Enqueue(Typed("pw-b"));

        await coordinator.SignInAgainAsync();

        // Disconnected first (keeping the base, no cloud delete, no confirm), then the connect path.
        Assert.Empty(_shell.Calls);
        Assert.Equal(["disconnect:False", "unlock:pw-b", "sync"], _sync.Log);
        Assert.Equal([PassphrasePromptKind.EnterExisting], _prompt.Calls.Select(c => c.Kind));
        Assert.Single(_auth.SignInHints);
        Assert.Equal(0, _auth.SignOutCalls);
        Assert.Equal(SyncStatusKind.Idle, coordinator.Status.Kind);
        Assert.Equal("B", _state.Value.AccountName);
        Assert.Equal("b@x.com", coordinator.Status.AccountLabel);
    }

    [Fact]
    public async Task SignInAgain_OtherAccount_Cancelled_DoesNotSync()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");
        _auth.OnSignIn = Returns("B", "b@x.com");

        await coordinator.SignInAgainAsync();

        Assert.Equal([PassphrasePromptKind.EnterExisting], _prompt.Calls.Select(c => c.Kind));
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
        Assert.False(_state.Value.IsConnected);
        Assert.Null(_secrets.Value);
        Assert.Equal(1, _auth.SignOutCalls);
        Assert.Null(_auth.Account);
        var infoCalls = _catalog.Provider.InfoCalls;

        await coordinator.SyncNowAsync(SyncTrigger.Timer);
        await coordinator.SyncNowAsync(SyncTrigger.Start);

        Assert.Equal(["disconnect:False"], _sync.Log);
        Assert.Equal(infoCalls, _catalog.Provider.InfoCalls);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
    }

    [Fact]
    public async Task SignInAgain_Fails_ShowsConnectFailedAndKeepsTheLink()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");
        _auth.OnSignIn = (_, _) => throw new SyncOfflineException();

        await coordinator.SignInAgainAsync();

        Assert.Equal([Strings.Sync_ConnectFailed], _feedback.Titles);
        Assert.Equal([Strings.Sync_SignInFailedDetail], _feedback.Details);
        Assert.Empty(_sync.Log);
        Assert.True(_state.Value.IsConnected);
        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
    }

    // ---- Disconnect and erase ----

    [Fact]
    public async Task Disconnect_SignsOut_AfterDisconnecting()
    {
        var coordinator = await CreateLinkedAsync(signedIn: true);
        _shell.Answers.Enqueue(true);
        _shell.Answers.Enqueue(false);   // keep the cloud data

        await coordinator.DisconnectAsync();

        Assert.Equal(["disconnect:False"], _sync.Log);
        Assert.Equal(["disconnect", "signout"], _order);
        Assert.False(_state.Value.IsConnected);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
    }

    [Fact]
    public async Task Disconnect_Cancelled_DoesNotSignOut()
    {
        var coordinator = await CreateLinkedAsync(signedIn: true);
        _shell.Answers.Enqueue(false);

        await coordinator.DisconnectAsync();

        Assert.Empty(_sync.Log);
        Assert.Equal(0, _auth.SignOutCalls);
        Assert.True(_state.Value.IsConnected);
    }

    [Fact]
    public async Task Disconnect_DeleteWhileSignedOut_ExplainsWhatToDo()
    {
        var coordinator = await CreateLinkedAsync(signedIn: false, lastAttempt: "AuthExpired");
        _shell.Answers.Enqueue(true);
        _shell.Answers.Enqueue(true);   // delete the cloud data

        await coordinator.DisconnectAsync();

        Assert.Equal([Strings.Sync_DisconnectFailed], _feedback.Titles);
        Assert.Equal([Strings.Sync_DisconnectSignInFirst], _feedback.Details);
        Assert.True(_state.Value.IsConnected);
        Assert.Equal("{}", _state.Value.BaseSnapshotJson);
        Assert.Equal(0, _auth.SignOutCalls);
        Assert.Equal(SyncStatusKind.SignInAgain, coordinator.Status.Kind);
    }

    [Fact]
    public async Task EraseAll_DoesNotSignOut()
    {
        var coordinator = await CreateLinkedAsync(signedIn: true);

        await coordinator.ForgetLinkForEraseAsync();

        Assert.Equal(0, _auth.SignOutCalls);
        Assert.Equal(SyncStatusKind.Off, coordinator.Status.Kind);
    }
}
