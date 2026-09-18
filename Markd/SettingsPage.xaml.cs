using Markd.Services;
using Markd.ViewModels;

namespace Markd;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<SettingsViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: true);
        SnackbarFeedbackService.Anchor = NavBar;
        await _viewModel.LoadAsync();
    }
}
