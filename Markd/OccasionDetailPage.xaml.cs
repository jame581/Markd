using Markd.Services;
using Markd.ViewModels;

namespace Markd;

[QueryProperty(nameof(OccasionIdQuery), "id")]
public partial class OccasionDetailPage : ContentPage
{
    private const string ShareItem = "Share milestone";
    private const string RemoveItem = "Remove milestone";

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
        switch (await menu.ShowAsync(OverflowButton, ["Share", "Delete occasion"], "Delete occasion"))
        {
            case "Share":
                await _viewModel.ShareCommand.ExecuteAsync(null);
                break;
            case "Delete occasion":
                await _viewModel.DeleteCommand.ExecuteAsync(null);
                break;
        }
    }

    private async void OnMilestoneTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View { BindingContext: MilestoneViewState state } view)
            return;

        var menu = ServiceHelper.GetRequiredService<IActionMenuService>();
        switch (await menu.ShowAsync(view, [ShareItem, RemoveItem], RemoveItem))
        {
            case ShareItem:
                await _viewModel.ShareMilestoneCommand.ExecuteAsync(state.Milestone);
                break;
            case RemoveItem:
                await _viewModel.RemoveMilestoneCommand.ExecuteAsync(state.Milestone);
                break;
        }
    }
}
