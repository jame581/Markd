using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Desktop.Controls;
using Markd.Desktop.Dialogs;
using Markd.Desktop.Views;
using Markd.Services;
using Markd.ViewModels;
using WinKeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using Windows.System;

namespace Markd.Desktop;

/// <summary>
/// The Windows root page: nav rail, header strip, the section host and the overlay layers. Interprets the
/// Shell-style routes the view models use ("OccasionFormPage?id=5", "OccasionDetailPage?id=5", "..", "//calendar").
/// </summary>
public partial class DesktopShellPage : ContentPage
{
    private readonly IAppSettingsService _settingsService;
    private readonly Dictionary<string, IDesktopSection> _sections = new();
    private readonly List<OverlayLayer> _overlays = [];
    private IDesktopSection? _current;
    private string? _currentKey;
    private OverlayLayer? _formLayer;
    private bool _formOpening;
    private ToastCard? _toast;

    public DesktopShellPage(IAppSettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        UpdateThemeItem();
        Application.Current!.RequestedThemeChanged += (_, _) => UpdateThemeItem();
        LocalizationManager.Instance.CultureChanged += (_, _) => UpdateThemeItem();
        Loaded += async (_, _) =>
        {
            if (_current is null)
                await ShowSectionAsync("home");
        };
        HandlerChanged += (_, _) => AttachKeyboardShortcuts();
    }

    private IEnumerable<RailItem> RailItems => [HomeItem, CalendarItem, CategoriesItem, SettingsItem, AboutItem];

    public async Task NavigateAsync(string route)
    {
        if (route == "..")
        {
            if (_formLayer is not null)
                await CloseFormAsync();
            else
                _current?.GoBack();
            return;
        }

        if (route.StartsWith("//", StringComparison.Ordinal))
        {
            await ShowSectionAsync(route[2..]);
            return;
        }

        var (name, id) = ParseRoute(route);
        switch (name)
        {
            case nameof(OccasionFormPage):
                await OpenFormAsync(id ?? 0);
                break;
            case nameof(OccasionDetailPage) when id is { } occasionId:
                await ShowSectionAsync("home");
                await ((HomeView)_sections["home"]).SelectAsync(occasionId, openDetail: true);
                break;
            case nameof(AboutPage):
                await ShowSectionAsync("about");
                break;
        }
    }

    public async Task ShowSectionAsync(string key)
    {
        if (_currentKey == key && _current is not null)
            return;

        var section = GetOrCreateSection(key);
        if (_current is not null)
        {
            _current.Hide();
            _current.HeaderChanged -= OnHeaderChanged;
            ((View)_current).IsVisible = false;
        }

        _current = section;
        _currentKey = key;
        ((View)section).IsVisible = true;
        section.HeaderChanged += OnHeaderChanged;
        foreach (var item in RailItems)
            item.IsSelected = item.Section == key;

        UpdateHeader();
        await section.ShowAsync();
        UpdateHeader();
    }

    /// <summary>
    /// Adds a modal layer and returns it at once; the entrance animation runs on its own, so a Cancel or Escape
    /// during the animation always finds the layer to close.
    /// </summary>
    public OverlayLayer ShowOverlay(View card, Action? onDismiss)
    {
        var layer = new OverlayLayer(card, onDismiss);
        layer.Closed += (_, _) =>
        {
            OverlayRoot.Children.Remove(layer);
            _overlays.Remove(layer);
            OverlayRoot.IsVisible = _overlays.Count > 0;

            // The dialog's view model can outlive it until the next GC and still react to messages (a language
            // switch re-labels the form's category picker). Disconnecting the handlers turns those late updates into
            // no-ops instead of calls into WinUI controls that are already disposed, which crash the app.
            layer.DisconnectHandlers();
        };

        _overlays.Add(layer);
        OverlayRoot.Children.Add(layer);
        OverlayRoot.IsVisible = true;
        _ = layer.PresentAsync();
        return layer;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string accept, string? cancel, bool destructive = false)
    {
        var dialog = new ConfirmDialog(title, message, accept, cancel, destructive);
        var layer = ShowOverlay(dialog, dialog.Cancel);
        var result = await dialog.Result;
        await layer.CloseAsync();
        return result;
    }

    public void ShowToast(string title, string? detail)
    {
        if (_toast is { } previous)
            ToastHost.Children.Remove(previous);

        var toast = new ToastCard(title, detail);
        toast.Closed += (_, _) =>
        {
            ToastHost.Children.Remove(toast);
            if (_toast == toast)
                _toast = null;
        };
        _toast = toast;
        ToastHost.Children.Add(toast);
        _ = toast.ShowAsync();
    }

    public async Task ShowMilestoneMomentAsync(MilestoneMoment moment)
    {
        var card = new MilestoneMomentCard(moment);
        var layer = ShowOverlay(card, card.Close);
        await card.Closed;
        await layer.CloseAsync();
    }

    public async Task<MilestoneEditorResult?> PromptMilestoneAsync()
    {
        var viewModel = ServiceHelper.GetRequiredService<MilestoneEditorViewModel>();
        var dialog = new MilestoneEditorDialog(viewModel);
        var layer = ShowOverlay(dialog, dialog.Cancel);
        var result = await viewModel.WaitForResultAsync();
        await layer.CloseAsync();
        return result;
    }

    public async Task<ExportChoice?> PromptExportAsync()
    {
        var dialog = new ExportOptionsDialog();
        var layer = ShowOverlay(dialog, dialog.Cancel);
        var result = await dialog.Result;
        await layer.CloseAsync();
        return result;
    }

    public async Task<string?> PromptPasswordAsync(string fileName, bool previousAttemptFailed)
    {
        var dialog = new PasswordDialog(fileName, previousAttemptFailed);
        var layer = ShowOverlay(dialog, dialog.Cancel);
        var result = await dialog.Result;
        await layer.CloseAsync();
        return result;
    }

    /// <summary>Cancels the topmost dialog, as Escape does. Returns false when no dialog is open.</summary>
    public bool DismissTopOverlay()
    {
        if (_overlays.LastOrDefault(o => !o.IsClosed) is not { } top)
            return false;

        top.Dismiss();
        return true;
    }

    public IDesktopSection? CurrentSection => _current;

    private async Task OpenFormAsync(int id)
    {
        // A second click while the form is loading must not open a second form.
        if (_formLayer is not null || _formOpening)
            return;

        _formOpening = true;
        try
        {
            var viewModel = ServiceHelper.GetRequiredService<OccasionFormViewModel>();
            await viewModel.InitializeAsync(id);
            var dialog = new OccasionFormDialog(viewModel);
            dialog.FitTo(Height);
            _formLayer = ShowOverlay(dialog, dialog.Cancel);
        }
        finally
        {
            _formOpening = false;
        }
    }

    private async Task CloseFormAsync()
    {
        if (_formLayer is not { } layer)
            return;

        _formLayer = null;
        await layer.CloseAsync();
    }

    private IDesktopSection GetOrCreateSection(string key)
    {
        if (_sections.TryGetValue(key, out var existing))
            return existing;

        IDesktopSection section = key switch
        {
            "calendar" => new CalendarView(),
            "categories" => new CategoriesView(ShowOverlay),
            "settings" => new SettingsView(),
            "about" => new AboutView(),
            _ => new HomeView()
        };

        _sections[key] = section;
        SectionHost.Children.Add((View)section);
        return section;
    }

    private void OnHeaderChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _current))
            UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (_current is not { } section)
            return;

        TitleLabel.Text = section.Title;
        SubtitleLabel.Text = section.Subtitle;
        SubtitleLabel.IsVisible = !string.IsNullOrEmpty(section.Subtitle);
        NewOccasionButton.IsVisible = section.ShowsNewOccasion;
        HeaderRule.Opacity = section.HasHeaderRule ? 1 : 0;
        BackCrumb.IsVisible = section.BackLabel is not null;
        BackLabel.Text = section.BackLabel;
    }

    private void UpdateThemeItem()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        ThemeItem.Glyph = dark ? "WinSun" : "WinMoon";
        var label = dark ? Strings.Desk_LightTheme : Strings.Desk_DarkTheme;
        ToolTipProperties.SetText(ThemeItem, label);
        SemanticProperties.SetDescription(ThemeItem, label);
    }

    private async void OnRailItemClicked(object? sender, EventArgs e)
    {
        if (sender is RailItem { Section: { } key })
        {
            // Clicking Home while an occasion is open full width returns to the list.
            if (key == _currentKey)
                _current?.GoBack();
            else
                await ShowSectionAsync(key);
        }
    }

    private async void OnThemeClicked(object? sender, EventArgs e)
    {
        var next = Application.Current?.RequestedTheme == AppTheme.Dark ? "Light" : "Dark";
        ThemeService.Apply(next);
        var settings = await _settingsService.GetAsync();
        settings.Theme = next;
        await _settingsService.SaveAsync(settings);

        // Settings keeps its own copy of the theme; reload it so its radio and its next save agree.
        if (_sections.TryGetValue("settings", out var settingsSection))
            await settingsSection.ShowAsync();
    }

    private async void OnNewOccasionClicked(object? sender, EventArgs e) => await OpenFormAsync(0);

    private void OnBackTapped(object? sender, TappedEventArgs e) => _current?.GoBack();

    private void AttachKeyboardShortcuts()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement root)
            return;

        var escape = new WinKeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (!DismissTopOverlay())
                _current?.GoBack();
        };

        var newOccasion = new WinKeyboardAccelerator { Key = VirtualKey.N, Modifiers = VirtualKeyModifiers.Control };
        newOccasion.Invoked += async (_, args) =>
        {
            args.Handled = true;
            if (_overlays.Count == 0)
                await OpenFormAsync(0);
        };

        root.KeyboardAccelerators.Add(escape);
        root.KeyboardAccelerators.Add(newOccasion);
        root.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
    }

    private static (string Name, int? Id) ParseRoute(string route)
    {
        var parts = route.Split('?', 2);
        int? id = null;
        if (parts.Length == 2)
        {
            foreach (var pair in parts[1].Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == "id" && int.TryParse(kv[1], out var value))
                    id = value;
            }
        }

        return (parts[0], id);
    }
}
