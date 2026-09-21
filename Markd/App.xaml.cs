using Markd.Core.Services;
using Markd.Services;

namespace Markd
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;
        private readonly NotificationService _notificationService;
        private readonly Queue<MilestoneMoment> _moments = new();
        private bool _showingMoment;

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
        }

        private async void OpenOccasion(int id)
        {
            await _services.GetRequiredService<IAppShellService>().GoToAsync($"{nameof(OccasionDetailPage)}?id={id}");
        }

        private async void OnWindowResumed(object? sender, EventArgs e)
        {
            await _notificationService.CheckOnOpenAsync();
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
