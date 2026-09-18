using Markd.Core.Localization;
using Markd.Services;
using Markd.ViewModels;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public partial class OccasionDetailPage : ContentPage
{
    private readonly OccasionDetailViewModel _viewModel;

    public OccasionDetailPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        BindingContext = _viewModel;
    }

    public string? OccasionIdQuery { get; set; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: false);
        SnackbarFeedbackService.Anchor = null;

        if (int.TryParse(OccasionIdQuery, out var id))
            await _viewModel.LoadAsync(id);

        // A stale notification can point at an occasion that has since been deleted.
        if (!_viewModel.HasOccasion)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        _viewModel.StartTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopTimer();
    }

    private async void OnOverflowClicked(object? sender, EventArgs e)
    {
        var menu = ServiceHelper.GetRequiredService<IActionMenuService>();
        string[] items = [Strings.Detail_Share, Strings.Detail_DeleteOccasion];
        var chosen = await menu.ShowAsync(OverflowButton, items, items[1]);
        if (chosen == items[0])
            await _viewModel.ShareCommand.ExecuteAsync(null);
        else if (chosen == items[1])
            await _viewModel.DeleteCommand.ExecuteAsync(null);
    }

    private async void OnMilestoneTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View { BindingContext: MilestoneViewState state } view)
            return;

        var menu = ServiceHelper.GetRequiredService<IActionMenuService>();
        string[] items = [Strings.Detail_ShareMilestone, Strings.Detail_RemoveMilestone];
        var chosen = await menu.ShowAsync(view, items, items[1]);
        if (chosen == items[0])
            await _viewModel.ShareMilestoneCommand.ExecuteAsync(state.Milestone);
        else if (chosen == items[1])
            await _viewModel.RemoveMilestoneCommand.ExecuteAsync(state.Milestone);
    }
}
