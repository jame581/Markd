using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Markd.Platforms.Windows
{
    /// <summary>
    /// Routes a click on a milestone toast to the occasion it names. The manifest declares the toast activator,
    /// so Windows hands every click to <see cref="AppNotificationManager"/>: the running app gets it as
    /// <c>NotificationInvoked</c>, and a closed app is started first and then gets it the same way (or, depending on
    /// the Windows build, as its activation arguments). Either can arrive before the MAUI app is ready, so the
    /// occasion id waits here until <see cref="Attach"/> is called.
    /// </summary>
    internal static class WindowsToastActivation
    {
        private static readonly object Sync = new();
        private static Action<int>? _open;
        private static int? _pending;

        /// <summary>Call once, as early as possible: registration must precede reading the activation arguments.</summary>
        public static void Initialize()
        {
            if (!WindowsToastScheduler.IsAvailable)
                return;

            try
            {
                AppNotificationManager.Default.NotificationInvoked += (_, args) => Deliver(args.Argument);
                AppNotificationManager.Default.Register();

                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activation.Kind == ExtendedActivationKind.AppNotification
                    && activation.Data is AppNotificationActivatedEventArgs args)
                {
                    Deliver(args.Argument);
                }
            }
            catch (Exception)
            {
                // Best effort: without it a click still starts the app, it just opens on the home screen.
            }
        }

        /// <summary>
        /// Releases the activator on exit. Microsoft's guidance for <see cref="AppNotificationManager.Register()"/> is to
        /// unregister before terminating so that later clicks launch the app again instead of targeting a dead process.
        /// </summary>
        public static void Shutdown()
        {
            if (!WindowsToastScheduler.IsAvailable)
                return;

            try
            {
                AppNotificationManager.Default.Unregister();
            }
            catch (Exception)
            {
                // Nothing to recover on the way out.
            }
        }

        /// <summary>Starts delivering clicks to <paramref name="open"/>, including one that launched the app.</summary>
        public static void Attach(Action<int> open)
        {
            int? pending;
            lock (Sync)
            {
                _open = open;
                pending = _pending;
                _pending = null;
            }

            if (pending is { } occasionId)
                open(occasionId);
        }

        private static void Deliver(string? argument)
        {
            // The toast's launch attribute is the bare occasion id (see WindowsToastScheduler.Build).
            if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var occasionId))
                return;

            Action<int>? open;
            lock (Sync)
            {
                open = _open;
                if (open is null)
                    _pending = occasionId;
            }

            if (open is null)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BringToFront();
                open(occasionId);
            });
        }

        // A click while the app runs arrives in the background; unlike a launch, nothing raises the window for us.
        private static void BringToFront()
        {
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
                return;

            if (window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
                presenter.Restore();

            window.Activate();
        }
    }
}
