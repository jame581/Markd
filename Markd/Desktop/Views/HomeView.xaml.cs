using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Localization;
using Markd.ViewModels;

namespace Markd.Desktop.Views;

/// <summary>
/// Home: a 380px list pane beside a live detail pane. Below 960px the detail pane collapses and an occasion
/// opens full width with a back affordance in the header. With no occasions the empty state replaces both panes.
/// </summary>
public partial class HomeView : ContentView, IDesktopSection
{
    private const double RailWidth = 56;
    private const double CollapseBelow = 960;

    private readonly OccasionListViewModel _list;
    private readonly OccasionDetailViewModel _detail;
    private int? _selectedId;
    private int? _pendingSelectId;
    private bool _narrow;
    private bool _showingNarrowDetail;

    public HomeView()
    {
        InitializeComponent();
        _list = ServiceHelper.GetRequiredService<OccasionListViewModel>();
        _detail = ServiceHelper.GetRequiredService<OccasionDetailViewModel>();
        BindingContext = _list;
        DetailPane.BindingContext = _detail;

        _list.Loaded += OnListLoaded;
        _list.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OccasionListViewModel.HomeSubtitle) or nameof(OccasionListViewModel.HasOccasions))
                RaiseHeaderChanged();
        };
        _detail.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OccasionDetailViewModel.Title) or nameof(OccasionDetailViewModel.CategoryName))
                RaiseHeaderChanged();
        };

        // A saved form selects the occasion it created or edited once the list reloads.
        WeakReferenceMessenger.Default.Register<HomeView, OccasionsChangedMessage>(this, (view, message) =>
        {
            if (message.Source is OccasionFormViewModel && message.OccasionId is { } id)
                view._pendingSelectId = id;
        });

        SizeChanged += (_, _) => UpdateLayout();
        LocalizationManager.Instance.CultureChanged += (_, _) => RaiseHeaderChanged();
    }

    public event EventHandler? HeaderChanged;

    public string Title => _showingNarrowDetail ? _detail.Title : Strings.Shell_Home;
    public string Subtitle => _showingNarrowDetail ? _detail.CategoryName : _list.HomeSubtitle;
    public bool ShowsNewOccasion => !_showingNarrowDetail && _list.HasOccasions;
    public bool HasHeaderRule => false;
    public string? BackLabel => _showingNarrowDetail ? Strings.Home_AllOccasions : null;

    public async Task ShowAsync()
    {
        await _list.LoadAsync();
        _detail.StartTimer();
    }

    public void Hide() => _detail.StopTimer();

    public bool GoBack()
    {
        if (!_showingNarrowDetail)
            return false;

        _showingNarrowDetail = false;
        UpdateLayout();
        return true;
    }

    /// <summary>Selects an occasion; in the collapsed layout <paramref name="openDetail"/> also opens it full width.</summary>
    public async Task SelectAsync(int id, bool openDetail)
    {
        if (_list.FindSummary(id) is null)
            return;

        _selectedId = id;
        foreach (var summary in _list.AllSummaries)
            summary.IsSelected = summary.Id == id;

        if (_detail.CurrentOccasion?.Id != id)
        {
            await _detail.LoadAsync(id);
            await DetailPane.ScrollToAsync(0, 0, false);
        }

        if (_narrow && openDetail)
            _showingNarrowDetail = true;

        UpdateLayout();
    }

    private async void OnListLoaded(object? sender, EventArgs e)
    {
        if (_list.AllSummaries.Count == 0)
        {
            _selectedId = null;
            _showingNarrowDetail = false;
            _detail.Clear();
            UpdateLayout();
            return;
        }

        var target = _pendingSelectId ?? _selectedId;
        _pendingSelectId = null;
        if (target is null || _list.FindSummary(target.Value) is null)
        {
            // Nothing chosen yet, or the selection was deleted: fall back to the pinned occasion, then the first row.
            _showingNarrowDetail = false;
            target = (_list.FeaturedSummary ?? _list.AllSummaries[0]).Id;
        }

        await SelectAsync(target.Value, openDetail: false);
    }

    private async void OnOccasionClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: OccasionSummary summary })
            await SelectAsync(summary.Id, openDetail: true);
    }

    private async void OnRemoveMilestoneClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: MilestoneViewState state })
            await _detail.RemoveMilestoneCommand.ExecuteAsync(state.Milestone);
    }

    private void UpdateLayout()
    {
        if (Width > 0)
            _narrow = Width + RailWidth < CollapseBelow;
        if (!_narrow)
            _showingNarrowDetail = false;

        var empty = _list.IsEmpty;
        EmptyState.IsVisible = empty;
        Panes.IsVisible = !empty;

        var columns = Panes.ColumnDefinitions;
        if (!_narrow)
        {
            columns[0].Width = new GridLength(380);
            columns[1].Width = new GridLength(1);
            columns[2].Width = GridLength.Star;
            ListPane.IsVisible = true;
            DetailPane.IsVisible = _detail.HasOccasion;
            ListStack.Padding = new Thickness(26, 18, 14, 40);
        }
        else if (_showingNarrowDetail)
        {
            columns[0].Width = new GridLength(0);
            columns[1].Width = new GridLength(0);
            columns[2].Width = GridLength.Star;
            ListPane.IsVisible = false;
            DetailPane.IsVisible = true;
        }
        else
        {
            columns[0].Width = GridLength.Star;
            columns[1].Width = new GridLength(0);
            columns[2].Width = new GridLength(0);
            ListPane.IsVisible = true;
            DetailPane.IsVisible = false;
            ListStack.Padding = new Thickness(26, 18, 26, 40);
        }

        PaneDivider.IsVisible = !_narrow;
        RaiseHeaderChanged();
    }

    private void RaiseHeaderChanged() => HeaderChanged?.Invoke(this, EventArgs.Empty);
}
