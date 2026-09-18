using AndroidX.Core.View;
using Markd.Controls;
using Microsoft.Maui.Platform;
using Android.Views;
using AView = Android.Views.View;

namespace Markd.Services;

public static partial class SystemBars
{
    static partial void ApplyPlatform(bool withNavigationBar, VisualElement? page)
    {
        if (Platform.CurrentActivity?.Window is not { } window)
            return;

        var dark = ThemeColors.IsDark;
#pragma warning disable CA1422 // Status/navigation bar colours still apply while the app opts out of edge-to-edge.
        window.SetStatusBarColor(ThemeColors.Get("PageBackground", dark).ToPlatform());
        window.SetNavigationBarColor(ThemeColors.Get(withNavigationBar ? "NavBarSurface" : "PageBackground", dark).ToPlatform());
#pragma warning restore CA1422
        if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            SetIconAppearance(controller, dark);

        // Modal pages live in their own dialog window, which the activity window's settings do not reach.
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && page?.Handler?.PlatformView is AView view)
            view.Post(() =>
            {
                const WindowInsetsControllerAppearance light =
                    WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars;
                view.WindowInsetsController?.SetSystemBarsAppearance(dark ? 0 : (int)light, (int)light);
            });
    }

    private static void SetIconAppearance(WindowInsetsControllerCompat controller, bool dark)
    {
        controller.AppearanceLightStatusBars = !dark;
        controller.AppearanceLightNavigationBars = !dark;
    }
}
