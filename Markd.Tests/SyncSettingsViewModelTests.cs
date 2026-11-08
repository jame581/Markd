using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Sync;
using Markd.Services.Sync;
using Markd.ViewModels;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Markd.Tests;

[Collection(nameof(GlobalCultureCollection))]
public class SyncSettingsViewModelTests
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeController : ISyncController
    {
        public SyncStatus Status { get; set; } = new(SyncStatusKind.Off, null, 0, null, null);
        public event EventHandler? StatusChanged;
        public List<string> Calls { get; } = [];
        public int Subscribers => StatusChanged?.GetInvocationList().Length ?? 0;
        public void Raise(SyncStatus status) { Status = status; StatusChanged?.Invoke(this, EventArgs.Empty); }
        public Task SyncNowAsync(SyncTrigger trigger) { Calls.Add("SyncNow:" + trigger); return Task.CompletedTask; }
        public Task OpenConflictsAsync() { Calls.Add("OpenConflicts"); return Task.CompletedTask; }
        public Task ConnectAsync(string providerId) { Calls.Add("Connect:" + providerId); return Task.CompletedTask; }
        public Task EnterPassphraseAsync() { Calls.Add("EnterPassphrase"); return Task.CompletedTask; }
        public Task ChangePassphraseAsync() { Calls.Add("ChangePassphrase"); return Task.CompletedTask; }
        public Task ReplaceCloudAsync() { Calls.Add("ReplaceCloud"); return Task.CompletedTask; }
        public Task DisconnectAsync() { Calls.Add("Disconnect"); return Task.CompletedTask; }
    }

    private sealed class FakeCatalog(params CloudProviderOption[] options) : ICloudProviderCatalog
    {
        public IReadOnlyList<CloudProviderOption> Available { get; } = options;
        public ICloudSyncProvider? Get(string providerId) => null;
        public string? DisplayNameOf(string providerId) => options.FirstOrDefault(o => o.Id == providerId)?.DisplayName;
    }

    private static readonly CloudProviderOption Folder = new("folder", "Local folder");

    private static SyncStatus S(SyncStatusKind kind, DateTime? last = null, int conflicts = 0) =>
        new(kind, last, conflicts, "Local folder", "me@example.com");

    private static (SyncSettingsViewModel Vm, FakeController Ctl, FakeTimeProvider Time) Create(
        SyncStatus? status = null, params CloudProviderOption[] options)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(Now));
        var ctl = new FakeController();
        if (status is not null) ctl.Status = status;
        var vm = new SyncSettingsViewModel(ctl, new FakeCatalog(options), time);
        return (vm, ctl, time);
    }

    [Theory]
    [InlineData(SyncStatusKind.Off, "Off")]
    [InlineData(SyncStatusKind.Syncing, "Syncing")]
    [InlineData(SyncStatusKind.NeedsPassphrase, "Enter the sync passphrase")]
    [InlineData(SyncStatusKind.SignInAgain, "Sign in again")]
    [InlineData(SyncStatusKind.Offline, "Offline, will retry")]
    [InlineData(SyncStatusKind.UpdateApp, "Cloud data is from a newer Markd: update the app")]
    [InlineData(SyncStatusKind.Unreadable, "Cloud data can't be read")]
    [InlineData(SyncStatusKind.Failed, "Sync failed, will retry")]
    public void Describe_MapsEveryKindInEnglish(SyncStatusKind kind, string expected)
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal(expected, SyncStatusText.Describe(S(kind), Now));
    }

    [Theory]
    [InlineData(0, "Synced just now")]
    [InlineData(59, "Synced just now")]
    [InlineData(60, "Synced 1 minute ago")]
    [InlineData(5 * 60, "Synced 5 minutes ago")]
    [InlineData(60 * 60, "Synced 1 hour ago")]
    [InlineData(3 * 3600, "Synced 3 hours ago")]
    [InlineData(24 * 3600, "Synced 1 day ago")]
    [InlineData(4 * 86400, "Synced 4 days ago")]
    public void Describe_IdleUsesRelativeTime(int secondsAgo, string expected)
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal(expected, SyncStatusText.Describe(S(SyncStatusKind.Idle, Now.AddSeconds(-secondsAgo)), Now));
    }

    [Fact]
    public void Describe_ConflictsUsesThePluralFamily()
    {
        using var _ = new CultureScope("en-GB");

        Assert.Equal("Sync paused: 1 conflict to resolve", SyncStatusText.Describe(S(SyncStatusKind.Conflicts, null, 1), Now));
        Assert.Equal("Sync paused: 3 conflicts to resolve", SyncStatusText.Describe(S(SyncStatusKind.Conflicts, null, 3), Now));
    }

    [Fact]
    public void Describe_UsesCzechPlurals()
    {
        using var _ = new CultureScope("cs-CZ");

        Assert.Equal("Synchronizováno před 1 minutou", SyncStatusText.Describe(S(SyncStatusKind.Idle, Now.AddMinutes(-1)), Now));
        Assert.Equal("Synchronizováno před 3 minutami", SyncStatusText.Describe(S(SyncStatusKind.Idle, Now.AddMinutes(-3)), Now));
        Assert.Equal("Synchronizace pozastavena: 2 konflikty k vyřešení", SyncStatusText.Describe(S(SyncStatusKind.Conflicts, null, 2), Now));
        Assert.Equal("Synchronizace pozastavena: 5 konfliktů k vyřešení", SyncStatusText.Describe(S(SyncStatusKind.Conflicts, null, 5), Now));
    }

    [Theory]
    [InlineData(SyncStatusKind.Conflicts, "Resolve")]
    [InlineData(SyncStatusKind.NeedsPassphrase, "Enter passphrase")]
    [InlineData(SyncStatusKind.SignInAgain, "Sign in again")]
    [InlineData(SyncStatusKind.Unreadable, "Replace cloud data")]
    [InlineData(SyncStatusKind.Offline, null)]
    [InlineData(SyncStatusKind.Idle, null)]
    public void ActionText_FollowsTheStatus(SyncStatusKind kind, string? expected)
    {
        using var _ = new CultureScope("en-GB");
        var (vm, _, _) = Create(S(kind), Folder);

        Assert.Equal(expected, vm.ActionText);
        Assert.Equal(expected is not null, vm.ActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ActionCommand_CallsTheMatchingControllerMethod()
    {
        using var _ = new CultureScope("en-GB");
        var (vm, ctl, _) = Create(S(SyncStatusKind.Conflicts, null, 2), Folder);
        await vm.ActionCommand.ExecuteAsync(null);
        ctl.Raise(S(SyncStatusKind.NeedsPassphrase));
        await vm.ActionCommand.ExecuteAsync(null);
        ctl.Raise(S(SyncStatusKind.SignInAgain));
        await vm.ActionCommand.ExecuteAsync(null);
        ctl.Raise(S(SyncStatusKind.Unreadable));
        await vm.ActionCommand.ExecuteAsync(null);

        Assert.Equal(["OpenConflicts", "EnterPassphrase", "Connect:folder", "ReplaceCloud"], ctl.Calls);
    }

    [Fact]
    public async Task Commands_CallTheController()
    {
        var (vm, ctl, _) = Create(S(SyncStatusKind.Idle, Now), Folder);

        await vm.ConnectCommand.ExecuteAsync(Folder);
        await vm.SyncNowCommand.ExecuteAsync(null);
        await vm.ChangePassphraseCommand.ExecuteAsync(null);
        await vm.ReplaceCloudCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);

        Assert.Equal(["Connect:folder", "SyncNow:Manual", "ChangePassphrase", "ReplaceCloud", "Disconnect"], ctl.Calls);
    }

    [Fact]
    public void AccountText_ShowsProviderAndEmail()
    {
        var (vm, _, _) = Create(new SyncStatus(SyncStatusKind.Idle, Now, 0, "Google Drive", "me@gmail.com"));
        Assert.Equal("Google Drive · me@gmail.com", vm.AccountText);

        var (noLabel, _, _) = Create(new SyncStatus(SyncStatusKind.Idle, Now, 0, "Google Drive", null));
        Assert.Equal("Google Drive", noLabel.AccountText);
    }

    [Fact]
    public void ProviderUnavailable_OffersDisconnectWithoutAnAction()
    {
        var (vm, _, _) = Create(new SyncStatus(SyncStatusKind.ProviderUnavailable, Now, 0, "Google Drive", "me@gmail.com"));

        Assert.Null(vm.ActionText);
        Assert.True(vm.IsConnected);
        Assert.True(vm.NeedsAttention);
        Assert.Equal("Google Drive isn't available on this device", vm.StatusText);
    }

    [Fact]
    public void State_FollowsTheStatus()
    {
        var (vm, ctl, _) = Create(null, Folder);
        Assert.True(vm.IsOff);
        Assert.False(vm.IsConnected);
        Assert.False(vm.NeedsAttention);

        ctl.Raise(S(SyncStatusKind.Idle, Now));
        Assert.False(vm.IsOff);
        Assert.True(vm.IsConnected);
        Assert.False(vm.NeedsAttention);
        Assert.False(vm.IsStatusWarning);
        Assert.Equal("Local folder · me@example.com", vm.AccountText);

        ctl.Raise(S(SyncStatusKind.Conflicts, null, 1));
        Assert.True(vm.NeedsAttention);
        Assert.True(vm.IsStatusWarning);
    }

    [Fact]
    public void IsVisible_RequiresAProvider()
    {
        Assert.False(Create(null).Vm.IsVisible);
        Assert.True(Create(null, Folder).Vm.IsVisible);
        Assert.Equal([Folder], Create(null, Folder).Vm.Providers);
    }

    [Fact]
    public void LanguageChange_RerendersTheStatusText()
    {
        using var en = new CultureScope("en-GB");
        var (vm, _, _) = Create(S(SyncStatusKind.Offline), Folder);
        Assert.Equal("Offline, will retry", vm.StatusText);

        using var cs = new CultureScope("cs-CZ");
        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage());

        Assert.Equal("Offline, zkusí to znovu", vm.StatusText);
    }

    [Fact]
    public void RelativeText_RefreshesEveryMinuteWhileAttached()
    {
        using var _ = new CultureScope("en-GB");
        var (vm, _, time) = Create(S(SyncStatusKind.Idle, Now), Folder);
        Assert.Equal("Synced just now", vm.StatusText);

        var raised = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(SyncSettingsViewModel.StatusText) or "") raised++; };

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, raised);
        Assert.Equal("Synced 1 minute ago", vm.StatusText);

        vm.Detach();
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Detach_UnsubscribesAndAttachResubscribes()
    {
        var (vm, ctl, _) = Create(null, Folder);
        Assert.Equal(1, ctl.Subscribers);

        vm.Detach();
        Assert.Equal(0, ctl.Subscribers);

        ctl.Status = S(SyncStatusKind.Idle, Now);
        vm.Attach();
        Assert.Equal(1, ctl.Subscribers);
        Assert.True(vm.IsConnected);
    }
}
