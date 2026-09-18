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
    }

    public event EventHandler? HeaderChanged
    {
        add { }
        remove { }
    }

    public string Title => "Settings";
    public string Subtitle => "Appearance, notifications and your local data";
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync() => _viewModel.LoadAsync();

    public void Hide()
    {
    }

    public bool GoBack() => false;
}
