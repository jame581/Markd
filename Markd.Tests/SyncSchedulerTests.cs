using Xunit;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Services.Sync;
using Markd.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Markd.Tests;

// The messenger is global and other test classes run in parallel and send the same messages, so the
// scheduler under test only accepts messages carrying a test-owned source and ignores everything else.
[Collection(nameof(SyncSchedulerCollection))]
public class SyncSchedulerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeController _controller = new();
    private readonly FakeNetwork _network = new();
    private readonly object _owned = new();
    private readonly object _ignored = new();
    private readonly SyncScheduler _scheduler;

    public SyncSchedulerTests()
    {
        _scheduler = new SyncScheduler(_controller, _network, _time, new InlineDispatcher(), s => !ReferenceEquals(s, _owned));
        _scheduler.Start();
    }

    public void Dispose() => _scheduler.Stop();

    private void Change(object? source = null) =>
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(Source: source ?? _owned));

    [Fact]
    public void Three_changes_within_the_debounce_make_one_run_after_the_last()
    {
        Change();
        _time.Advance(TimeSpan.FromSeconds(6));
        Change();
        _time.Advance(TimeSpan.FromSeconds(6));
        Change();
        _time.Advance(TimeSpan.FromSeconds(9));
        Assert.Empty(_controller.Triggers);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal([SyncTrigger.LocalChange], _controller.Triggers);

        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(_controller.Triggers);
    }

    [Fact]
    public void Category_changes_also_debounce()
    {
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(_owned));
        _time.Advance(SyncScheduler.LocalChangeDebounce);
        Assert.Equal([SyncTrigger.LocalChange], _controller.Triggers);
    }

    [Fact]
    public void Ignored_sources_trigger_nothing()
    {
        Change(_ignored);
        Change(new object());
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage(_ignored));
        WeakReferenceMessenger.Default.Send(new CategoriesChangedMessage());
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Empty(_controller.Triggers);
    }

    [Fact]
    public void Timer_fires_every_fifteen_minutes()
    {
        _time.Advance(TimeSpan.FromMinutes(14));
        Assert.Empty(_controller.Triggers);
        _time.Advance(TimeSpan.FromMinutes(1));
        _time.Advance(SyncScheduler.TimerInterval);
        Assert.Equal([SyncTrigger.Timer, SyncTrigger.Timer], _controller.Triggers);
    }

    [Fact]
    public void Becoming_online_triggers_connectivity()
    {
        _network.RaiseOnline();
        Assert.Equal([SyncTrigger.Connectivity], _controller.Triggers);
    }

    [Fact]
    public async Task Start_and_resume_map_to_their_triggers()
    {
        await _scheduler.OnAppStartAsync();
        await _scheduler.OnAppResumedAsync();
        Assert.Equal([SyncTrigger.Start, SyncTrigger.Resume], _controller.Triggers);
    }

    [Fact]
    public void Stop_cancels_pending_debounce_timer_messages_and_connectivity()
    {
        Change();
        _scheduler.Stop();
        Change();
        _network.RaiseOnline();
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Empty(_controller.Triggers);
    }

    [Fact]
    public async Task Start_and_resume_do_nothing_after_stop()
    {
        _scheduler.Stop();
        await _scheduler.OnAppStartAsync();
        await _scheduler.OnAppResumedAsync();
        Assert.Empty(_controller.Triggers);
    }

    [Fact]
    public void Start_twice_does_not_double_register()
    {
        _scheduler.Start();
        _network.RaiseOnline();
        Assert.Single(_controller.Triggers);
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class FakeNetwork : INetworkStatus
    {
        public bool IsOnline => true;

        public event EventHandler? BecameOnline;

        public void RaiseOnline() => BecameOnline?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeController : ISyncController
    {
        public List<SyncTrigger> Triggers { get; } = [];

        public SyncStatus Status => throw new NotSupportedException();

        public event EventHandler? StatusChanged { add { } remove { } }

        public Task SyncNowAsync(SyncTrigger trigger)
        {
            Triggers.Add(trigger);
            return Task.CompletedTask;
        }

        public Task OpenConflictsAsync() => throw new NotSupportedException();
        public Task ConnectAsync(string providerId) => throw new NotSupportedException();
        public Task EnterPassphraseAsync() => throw new NotSupportedException();
        public Task ChangePassphraseAsync() => throw new NotSupportedException();
        public Task ReplaceCloudAsync() => throw new NotSupportedException();
        public Task DisconnectAsync() => throw new NotSupportedException();
    }
}

[CollectionDefinition(nameof(SyncSchedulerCollection))]
public class SyncSchedulerCollection;
