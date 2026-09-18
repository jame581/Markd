namespace Markd.Services;

/// <summary>Colours the Android status and navigation bars to match the page surface; a no-op elsewhere.</summary>
public static partial class SystemBars
{
    private static bool _withNavigationBar;
    private static WeakReference<VisualElement>? _page;
    private static bool _followsTheme;

    /// <param name="withNavigationBar">True on the four tab destinations, where the system bar continues the app's navigation bar.</param>
    /// <param name="page">The page applying the bars; needed for modal pages, which Android shows in their own window.</param>
    public static void Apply(bool withNavigationBar, VisualElement? page = null)
    {
        _withNavigationBar = withNavigationBar;
        _page = page is null ? null : new WeakReference<VisualElement>(page);
        ApplyPlatform(withNavigationBar, page);

        // A theme change while a page is open recolours the bars for that page. RequestedThemeChanged is a weak
        // event, so the handler is a static method: a capturing lambda would be collected and silently unsubscribed.
        if (!_followsTheme && Application.Current is { } app)
        {
            _followsTheme = true;
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    private static void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e) =>
        Application.Current?.Dispatcher.Dispatch(() =>
        {
            VisualElement? page = null;
            _page?.TryGetTarget(out page);
            ApplyPlatform(_withNavigationBar, page);
        });

    static partial void ApplyPlatform(bool withNavigationBar, VisualElement? page);
}
