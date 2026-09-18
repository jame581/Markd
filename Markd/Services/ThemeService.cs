namespace Markd.Services;

public static class ThemeService
{
    public static void Apply(string? theme)
    {
        if (Application.Current is null)
            return;

        Application.Current.UserAppTheme = theme switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}
