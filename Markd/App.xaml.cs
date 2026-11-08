using Markd.Core.Services;
using Markd.Services;
using Markd.Services.Sync;

namespace Markd
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;
        private readonly NotificationService _notificationService;
        private readonly Queue<MilestoneMoment> _moments = new();
        private bool _showingMoment;
        private bool _syncStarted;

        public App(IServiceProvider services, NotificationService notificationService)
        {
            InitializeComponent();

            _services = services;
            _notificationService = notificationService;
            _notificationService.MilestoneReached += (_, e) =>
                EnqueueMoment(new MilestoneMoment(e.Occasion, e.Milestone, e.NextMilestone, e.Days));
            _notificationService.OpenOccasionRequested += (_, id) => OpenOccasion(id);
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Apply the saved theme before the first page renders. SQLite access is synchronous under the hood.
            var settings = _services.GetRequiredService<IAppSettingsService>().GetAsync().GetAwaiter().GetResult();
            ThemeService.Apply(settings.Theme);
            LanguageService.Apply(settings.Language);

#if WINDOWS
            var window = Desktop.DesktopWindow.Create(_services);
            window.Destroying += (_, _) => Platforms.Windows.WindowsToastActivation.Shutdown();
#else
            var window = new Window(_services.GetRequiredService<AppShell>());
#endif
            window.Resumed += OnWindowResumed;
            window.Destroying += (_, _) => StopSync();
            return window;
        }

        protected override async void OnStart()
        {
            base.OnStart();
            ShareSheet.ClearStaged();
#if WINDOWS
            // A toast click that launched the app is delivered here, once the shell exists to navigate.
            Platforms.Windows.WindowsToastActivation.Attach(OpenOccasion);
#endif
            _notificationService.StartClock();
            await _notificationService.CheckOnOpenAsync();
            await _notificationService.RescheduleAsync();
            await StartSyncAsync();
        }

        /// <summary>Reads the stored sync state, starts the triggers and runs the start-up sync. Never breaks startup.</summary>
        private async Task StartSyncAsync()
        {
            try
            {
                await _services.GetRequiredService<SyncCoordinator>().InitializeAsync();
                var scheduler = _services.GetRequiredService<SyncScheduler>();
                scheduler.Start();
                _syncStarted = true;
                await scheduler.OnAppStartAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Starting sync failed: {ex}");
            }
        }

        private void StopSync()
        {
            try
            {
                _services.GetRequiredService<SyncScheduler>().Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stopping sync failed: {ex}");
            }
        }

        private async void OpenOccasion(int id)
        {
            await _services.GetRequiredService<IAppShellService>().GoToAsync($"{nameof(OccasionDetailPage)}?id={id}");
        }

        private async void OnWindowResumed(object? sender, EventArgs e)
        {
            await _notificationService.CheckOnOpenAsync();
            try
            {
                var scheduler = _services.GetRequiredService<SyncScheduler>();
                // A window destroyed and recreated by the platform (Android) stopped the triggers; Start is idempotent.
                if (_syncStarted)
                    scheduler.Start();
                await scheduler.OnAppResumedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"The resume sync failed: {ex}");
            }
        }

        private void EnqueueMoment(MilestoneMoment moment)
        {
            _moments.Enqueue(moment);
            Dispatcher.Dispatch(async () =>
            {
                if (_showingMoment)
                    return;

                _showingMoment = true;
                try
                {
                    var presenter = _services.GetRequiredService<IMilestoneMomentPresenter>();
                    while (_moments.TryDequeue(out var next))
                        await presenter.ShowAsync(next);
                }
                finally
                {
                    _showingMoment = false;
                }
            });
        }
    }
}
