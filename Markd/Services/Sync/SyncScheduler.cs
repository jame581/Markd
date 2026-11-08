using CommunityToolkit.Mvvm.Messaging;
using Markd.ViewModels;

namespace Markd.Services.Sync;

/// <summary>
/// Decides when to ask the sync controller to run: app start and resume, debounced local changes, a periodic
/// timer and the device coming back online. All calls hop through the UI dispatcher.
/// </summary>
public sealed class SyncScheduler(
    ISyncController controller,
    INetworkStatus network,
    TimeProvider time,
    IUiDispatcher dispatcher,
    Func<object?, bool> isIgnoredSource)
{
    public static readonly TimeSpan LocalChangeDebounce = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TimerInterval = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private ITimer? _debounce;
    private ITimer? _periodic;
    private bool _running;

    public void Start()
    {
        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _debounce = time.CreateTimer(_ => Fire(SyncTrigger.LocalChange), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _periodic = time.CreateTimer(_ => Fire(SyncTrigger.Timer), null, TimerInterval, TimerInterval);
        }

        WeakReferenceMessenger.Default.Register<SyncScheduler, OccasionsChangedMessage>(this, static (r, m) => r.OnLocalChange(m.Source));
        WeakReferenceMessenger.Default.Register<SyncScheduler, CategoriesChangedMessage>(this, static (r, m) => r.OnLocalChange(m.Source));
        network.BecameOnline += OnBecameOnline;
    }

    public void Stop()
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        network.BecameOnline -= OnBecameOnline;

        lock (_gate)
        {
            _running = false;
            _debounce?.Dispose();
            _periodic?.Dispose();
            _debounce = null;
            _periodic = null;
        }
    }

    public Task OnAppStartAsync() => RunIfStartedAsync(SyncTrigger.Start);

    public Task OnAppResumedAsync() => RunIfStartedAsync(SyncTrigger.Resume);

    private Task RunIfStartedAsync(SyncTrigger trigger)
    {
        lock (_gate)
        {
            if (!_running)
            {
                return Task.CompletedTask;
            }
        }

        return RunAsync(trigger);
    }

    private void OnLocalChange(object? source)
    {
        if (isIgnoredSource(source))
        {
            return;
        }

        lock (_gate)
        {
            _debounce?.Change(LocalChangeDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnBecameOnline(object? sender, EventArgs e) => Fire(SyncTrigger.Connectivity);

    private void Fire(SyncTrigger trigger)
    {
        lock (_gate)
        {
            if (!_running)
            {
                return;
            }
        }

        _ = RunAsync(trigger);
    }

    private async Task RunAsync(SyncTrigger trigger)
    {
        try
        {
            await dispatcher.InvokeAsync(() => controller.SyncNowAsync(trigger));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Sync scheduler: {trigger} run failed: {ex}");
            // The controller never throws, so only a dispatcher failure lands here and must not crash a timer callback.
        }
    }
}
