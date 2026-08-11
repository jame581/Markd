using Markd.Services;

namespace Markd
{
    public partial class App : Application
    {
        private readonly AppShell _appShell;
        private readonly NotificationService _notificationService;

        public App(AppShell appShell, NotificationService notificationService)
        {
            _appShell = appShell;
            _notificationService = notificationService;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(_appShell);
            window.Resumed += OnWindowResumed;
            return window;
        }

        protected override async void OnStart()
        {
            base.OnStart();
            await _notificationService.CheckAndNotifyAsync();
        }

        private async void OnWindowResumed(object? sender, EventArgs e)
        {
            await _notificationService.CheckAndNotifyAsync();
        }
    }
}
