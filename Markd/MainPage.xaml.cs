using Markd.Core.Localization;
using Markd.Services;
using Markd.Services.Sync;
using Markd.ViewModels;

namespace Markd;

public partial class MainPage : ContentPage
{
    private readonly OccasionListViewModel _viewModel;

    public MainPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<OccasionListViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: true);
        SnackbarFeedbackService.Anchor = NavBar;
        await _viewModel.LoadAsync();
        _viewModel.StartTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopTimer();
    }

    private async void OnFeaturedTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.FeaturedSummary is { } featured)
            await _viewModel.OpenDetailCommand.ExecuteAsync(featured);
    }

    private async void OnSyncIconTapped(object? sender, EventArgs e)
    {
        var sync = ServiceHelper.GetRequiredService<SyncCoordinator>();
        if (sync.Status.Kind == SyncStatusKind.Conflicts)
            await sync.OpenConflictsAsync();
        else
            await Shell.Current.GoToAsync("//settings", false);
    }

    private async void OnOverflowClicked(object? sender, EventArgs e)
    {
        var menu = ServiceHelper.GetRequiredService<IActionMenuService>();
        if (await menu.ShowAsync(OverflowButton, [Strings.Home_AboutMarkd]) == Strings.Home_AboutMarkd)
            await Shell.Current.GoToAsync(nameof(AboutPage));
    }
}
