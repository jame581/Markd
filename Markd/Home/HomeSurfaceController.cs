using Markd.ViewModels;

namespace Markd.Home;

public sealed class HomeSurfaceController
{
    private const double SplitViewBreakpoint = 960;

    private readonly OccasionListViewModel _viewModel;
    private readonly IHomeSurfaceDetailPresenter _detailPresenter;
    private readonly bool _supportsSplitView;
    private OccasionSummary? _selectedSummary;
    private bool _isTwoPane;

    public HomeSurfaceController(
        OccasionListViewModel viewModel,
        IHomeSurfaceDetailPresenter detailPresenter,
        bool supportsSplitView)
    {
        _viewModel = viewModel;
        _detailPresenter = detailPresenter;
        _supportsSplitView = supportsSplitView;
        State = new HomeSurfaceState(false, false, false, null);
    }

    public HomeSurfaceState State { get; private set; }

    public OccasionSummary? ActiveSummary => State.SelectedSummary;

    public async Task<HomeSurfaceState> InitializeAsync(double width)
    {
        await _viewModel.LoadAsync();
        return await HandleWidthChangedAsync(width);
    }

    public async Task<HomeSurfaceState> HandleWidthChangedAsync(double width)
    {
        _isTwoPane = _supportsSplitView && width >= SplitViewBreakpoint;
        return await RefreshStateAsync();
    }

    public async Task<HomeSurfaceInteraction> HandleOccasionSelectedAsync(OccasionSummary summary)
    {
        _selectedSummary = FindSummaryById(summary.Occasion.Id) ?? summary;

        if (_isTwoPane)
        {
            var state = await RefreshStateAsync();
            return new HomeSurfaceInteraction(state, NavigateToDetail: false, ClearListSelection: false, NavigationTarget: null);
        }

        var singlePaneState = BuildState(_viewModel.FeaturedSummary);
        return new HomeSurfaceInteraction(singlePaneState, NavigateToDetail: true, ClearListSelection: true, NavigationTarget: summary);
    }

    public async Task<HomeSurfaceInteraction> HandleFeaturedInvokedAsync()
    {
        if (_viewModel.FeaturedSummary is not { } featured)
            return new HomeSurfaceInteraction(State, NavigateToDetail: false, ClearListSelection: false, NavigationTarget: null);

        _selectedSummary = featured;

        if (_isTwoPane)
        {
            var state = await RefreshStateAsync();
            return new HomeSurfaceInteraction(state, NavigateToDetail: false, ClearListSelection: false, NavigationTarget: null);
        }

        return new HomeSurfaceInteraction(BuildState(featured), NavigateToDetail: true, ClearListSelection: false, NavigationTarget: featured);
    }

    public void Stop()
    {
        _detailPresenter.Stop();
    }

    private async Task<HomeSurfaceState> RefreshStateAsync()
    {
        var target = _isTwoPane
            ? (_selectedSummary is null ? null : FindSummaryById(_selectedSummary.Occasion.Id))
                ?? _viewModel.FeaturedSummary
                ?? TryGetFirstSummary()
            : _viewModel.FeaturedSummary;

        if (_isTwoPane && target is not null)
        {
            await EnsureDetailLoadedAsync(target);
        }
        else
        {
            _detailPresenter.Stop();
        }

        State = BuildState(target);
        return State;
    }

    private async Task EnsureDetailLoadedAsync(OccasionSummary summary)
    {
        if (_detailPresenter.CurrentOccasionId == summary.Occasion.Id)
            return;

        await _detailPresenter.LoadAsync(summary);
    }

    private HomeSurfaceState BuildState(OccasionSummary? target) =>
        new(
            IsTwoPane: _isTwoPane,
            ShowMobileFeaturedCard: !_isTwoPane && _viewModel.HasFeaturedOccasion,
            ShowDesktopDetailPane: _isTwoPane && target is not null,
            SelectedSummary: _isTwoPane ? target : null);

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
}

public sealed record HomeSurfaceInteraction(
    HomeSurfaceState State,
    bool NavigateToDetail,
    bool ClearListSelection,
    OccasionSummary? NavigationTarget);

public interface IHomeSurfaceDetailPresenter
{
    int? CurrentOccasionId { get; }
    Task LoadAsync(OccasionSummary summary);
    void Stop();
}
