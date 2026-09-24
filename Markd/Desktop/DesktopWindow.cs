using Markd.Controls;

namespace Markd.Desktop;

/// <summary>
/// Creates the desktop window: 1320×840, content extended into a 40px <see cref="TitleBar"/> on the
/// WindowChrome ground, with the 16px accent "M" mark and the app name at 12px Segoe UI.
/// </summary>
public static class DesktopWindow
{
    public static Window Create(IServiceProvider services)
    {
        var page = services.GetRequiredService<DesktopShellPage>();
        var window = new Window(page)
        {
            Title = "Markd Day Counter",
            Width = 1320,
            Height = 840,
            MinimumWidth = 720,
            MinimumHeight = 560,
            TitleBar = CreateTitleBar()
        };

        var display = DeviceDisplay.Current.MainDisplayInfo;
        if (display.Width > 0 && display.Density > 0)
        {
            window.X = Math.Max(0, (display.Width / display.Density - window.Width) / 2);
            window.Y = Math.Max(0, (display.Height / display.Density - window.Height) / 2);
        }

        return window;
    }

    private static TitleBar CreateTitleBar()
    {
        var mark = new Border
        {
            WidthRequest = 16,
            HeightRequest = 16,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "M",
                FontFamily = "FigtreeExtraBold",
                FontSize = 10,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        ThemeColors.Bind(mark, VisualElement.BackgroundColorProperty, "AccentFill");

        var name = new Label { Text = "Markd Day Counter", FontFamily = "Segoe UI", FontSize = 12, VerticalOptions = LayoutOptions.Center };
        ThemeColors.Bind(name, Label.TextColorProperty, "TextPrimary");

        var titleBar = new TitleBar
        {
            HeightRequest = 40,
            LeadingContent = new HorizontalStackLayout
            {
                Spacing = 10,
                Padding = new Thickness(14, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center,
                Children = { mark, name }
            }
        };
        ThemeColors.Bind(titleBar, VisualElement.BackgroundColorProperty, "WindowChrome");
        ThemeColors.Bind(titleBar, TitleBar.ForegroundColorProperty, "TextPrimary");
        return titleBar;
    }
}
