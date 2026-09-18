namespace Markd.Controls;

/// <summary>Binds a colour property to a Light/Dark token pair from Colors.xaml ("AccentFill" → AccentFillLight / AccentFillDark).</summary>
public static class ThemeColors
{
    public static Color Get(string key, bool dark)
    {
        var resources = Application.Current!.Resources;
        var name = key + (dark ? "Dark" : "Light");
        if (resources.TryGetValue(name, out var value) && value is Color color)
            return color;

        return resources.TryGetValue(key, out var single) && single is Color fallback ? fallback : Colors.Magenta;
    }

    public static void Bind(BindableObject target, BindableProperty property, string key) =>
        target.SetAppThemeColor(property, Get(key, false), Get(key, true));

    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;
}
