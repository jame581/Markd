using Markd.ViewModels;
using Microsoft.Maui.Devices;

namespace Markd;

public partial class MainPage : ContentPage
{
    private const double SplitViewBreakpoint = 960;

    private readonly OccasionListViewModel _viewModel;
    private readonly OccasionDetailViewModel _detailViewModel;
    private OccasionSummary? _selectedSummary;
    private bool _isTwoPane;
    private bool _isSyncingSelection;

    public MainPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.GetRequiredService<OccasionListViewModel>();
        _detailViewModel = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();

        BindingContext = _viewModel;
        DesktopDetailRoot.BindingContext = _detailViewModel;
        Title = "Markd";

        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync();
        UpdateLayoutState(Width);
        await RefreshDetailAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _detailViewModel.StopTimer();
    }

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        var layoutChanged = UpdateLayoutState(Width);

        if (layoutChanged)
            await RefreshDetailAsync();
    }

    private async void OnOccasionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        if (e.CurrentSelection.FirstOrDefault() is not OccasionSummary summary)
            return;

        _selectedSummary = summary;

        if (_isTwoPane)
        {
            await LoadDetailAsync(summary);
            return;
        }

        await _viewModel.OpenDetailCommand.ExecuteAsync(summary);

        if (sender is CollectionView collectionView)
            collectionView.SelectedItem = null;
    }

    private async void OnFeaturedCardTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel.FeaturedSummary is not { } featured)
            return;

        if (_isTwoPane)
        {
            _selectedSummary = featured;
            SetSelectedItem(featured);
            await LoadDetailAsync(featured);
            return;
        }

        await _viewModel.OpenDetailCommand.ExecuteAsync(featured);
    }

    private async void OnOpenDetailClicked(object? sender, EventArgs e)
    {
        if (_selectedSummary is null)
            return;

        await _viewModel.OpenDetailCommand.ExecuteAsync(_selectedSummary);
    }

    private bool UpdateLayoutState(double width)
    {
        var shouldUseTwoPane = DeviceInfo.Current.Platform == DevicePlatform.WinUI && width >= SplitViewBreakpoint;

        if (_isTwoPane == shouldUseTwoPane)
        {
            ApplyLayoutVisibility();
            return false;
        }

        _isTwoPane = shouldUseTwoPane;
        ListPaneColumn.Width = shouldUseTwoPane ? 380 : GridLength.Star;
        DetailPaneColumn.Width = shouldUseTwoPane ? GridLength.Star : new GridLength(0);
        HomeLayoutGrid.ColumnSpacing = shouldUseTwoPane ? 24 : 0;

        if (!shouldUseTwoPane)
            OccasionGroupsView.SelectedItem = null;

        ApplyLayoutVisibility();
        return true;
    }

    private void ApplyLayoutVisibility()
    {
        var hasAnyOccasions = TryGetFirstSummary() is not null;
        MobileFeaturedCard.IsVisible = !_isTwoPane && _viewModel.HasFeaturedOccasion;
        DesktopDetailScroll.IsVisible = _isTwoPane && hasAnyOccasions && _detailViewModel.CurrentOccasion is not null;
    }

    private async Task RefreshDetailAsync()
    {
        var selectedSummary = _selectedSummary is null
            ? null
            : FindSummaryById(_selectedSummary.Occasion.Id);

        var target = _isTwoPane
            ? selectedSummary ?? _viewModel.FeaturedSummary ?? TryGetFirstSummary()
            : _viewModel.FeaturedSummary;

        if (target is null)
        {
            _detailViewModel.StopTimer();
            DesktopDetailScroll.IsVisible = false;
            return;
        }

        if (_isTwoPane && !ReferenceEquals(OccasionGroupsView.SelectedItem, target))
            SetSelectedItem(target);

        await LoadDetailAsync(target);
        ApplyLayoutVisibility();
    }

    private async Task LoadDetailAsync(OccasionSummary summary)
    {
        _selectedSummary = FindSummaryById(summary.Occasion.Id) ?? summary;

        if (_detailViewModel.CurrentOccasion?.Id == summary.Occasion.Id)
        {
            ApplyLayoutVisibility();
            return;
        }

        _detailViewModel.StopTimer();
        await _detailViewModel.LoadAsync(summary.Occasion.Id);
        _detailViewModel.StartTimer();
        ApplyLayoutVisibility();
    }

    private OccasionSummary? TryGetFirstSummary()
    {
        foreach (var group in _viewModel.OccasionGroups)
        {
            if (group.Count > 0)
                return group[0];
        }

        return null;
    }

    private OccasionSummary? FindSummaryById(int occasionId)
    {
        foreach (var group in _viewModel.OccasionGroups)
        {
            var match = group.FirstOrDefault(summary => summary.Occasion.Id == occasionId);
            if (match is not null)
                return match;
        }

        return null;
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
