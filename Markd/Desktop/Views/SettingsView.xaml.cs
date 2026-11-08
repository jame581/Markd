using Markd.Core.Localization;
using Markd.ViewModels;

namespace Markd.Desktop.Views;

/// <summary>Settings as a single column of cards. Every change saves immediately; there is no Save button.</summary>
public partial class SettingsView : ContentView, IDesktopSection
{
    private readonly SettingsViewModel _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<SettingsViewModel>();
        BindingContext = _viewModel;
        LocalizationManager.Instance.CultureChanged += (_, _) => HeaderChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? HeaderChanged;

    public string Title => Strings.Settings_Title;
    public string Subtitle => Strings.Settings_SubtitleDesktop;
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync()
    {
        // The theme toggle reloads settings while another section is shown: only a visible section follows sync.
        if (IsVisible)
            _viewModel.Sync.Attach();
        return _viewModel.LoadAsync();
    }

    /// <summary>The sync section stops following the status while another section is shown.</summary>
    public void Hide() => _viewModel.Sync.Detach();

    public bool GoBack() => false;
}
