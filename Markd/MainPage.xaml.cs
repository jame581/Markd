using Markd.Home;
using Markd.ViewModels;
using Microsoft.Maui.Devices;

namespace Markd;

public partial class MainPage : ContentPage
{
    private readonly OccasionListViewModel _viewModel;
    private readonly OccasionDetailViewModel _detailViewModel;
    private readonly HomeSurfaceController _controller;
    private bool _isSyncingSelection;

    public MainPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.GetRequiredService<OccasionListViewModel>();
        _detailViewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        _controller = new HomeSurfaceController(
            _viewModel,
            new HomeSurfaceDetailPresenter(_detailViewModel),
            DeviceInfo.Current.Platform == DevicePlatform.WinUI);

        BindingContext = _viewModel;
        DesktopDetailRoot.BindingContext = _detailViewModel;
        Title = "Markd";

        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await ApplyStateAsync(await _controller.InitializeAsync(Width));
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _controller.Stop();
    }

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        await ApplyStateAsync(await _controller.HandleWidthChangedAsync(Width));
    }

    private async void OnOccasionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        if (e.CurrentSelection.FirstOrDefault() is not OccasionSummary summary)
            return;

        var interaction = await _controller.HandleOccasionSelectedAsync(summary);
        await ApplyInteractionAsync(interaction);
    }

    private async void OnFeaturedCardTapped(object? sender, TappedEventArgs e)
    {
        await ApplyInteractionAsync(await _controller.HandleFeaturedInvokedAsync());
    }

    private async void OnOpenDetailClicked(object? sender, EventArgs e)
    {
        if (_controller.ActiveSummary is not { } summary)
            return;

        await _viewModel.OpenDetailCommand.ExecuteAsync(summary);
    }

    private async Task ApplyInteractionAsync(HomeSurfaceInteraction interaction)
    {
        await ApplyStateAsync(interaction.State);

        if (interaction.NavigateToDetail && interaction.NavigationTarget is not null)
            await _viewModel.OpenDetailCommand.ExecuteAsync(interaction.NavigationTarget);

        if (interaction.ClearListSelection)
            SetSelectedItem(null);
    }

    private Task ApplyStateAsync(HomeSurfaceState state)
    {
        ListPaneColumn.Width = state.IsTwoPane ? 380 : GridLength.Star;
        DetailPaneColumn.Width = state.IsTwoPane ? GridLength.Star : new GridLength(0);
        HomeLayoutGrid.ColumnSpacing = state.IsTwoPane ? 24 : 0;
        MobileFeaturedCard.IsVisible = state.ShowMobileFeaturedCard;
        DesktopDetailScroll.IsVisible = state.ShowDesktopDetailPane && _detailViewModel.CurrentOccasion is not null;

        SetSelectedItem(state.SelectedSummary);
        return Task.CompletedTask;
    }

    private void SetSelectedItem(OccasionSummary? summary)
    {
        _isSyncingSelection = true;
        try
        {
            OccasionGroupsView.SelectedItem = summary;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}
