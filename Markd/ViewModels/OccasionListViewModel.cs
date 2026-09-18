using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Services;
using Markd.Services;

namespace Markd.ViewModels;

public class OccasionListViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private readonly ICategoryService _categoryService;
    private readonly IAppShellService _shellService;
    private OccasionSummary? _featuredSummary;
    private string _featuredBreakdown = string.Empty;
    private string _homeSubtitle = string.Empty;
    private bool _hasLoaded;
    private IDispatcherTimer? _timer;

    public OccasionListViewModel(IOccasionService occasionService, ICategoryService categoryService, IAppShellService shellService)
    {
        _occasionService = occasionService;
        _categoryService = categoryService;
        _shellService = shellService;
        AddCommand = new AsyncRelayCommand(AddAsync);
        OpenDetailCommand = new AsyncRelayCommand<OccasionSummary?>(OpenDetailAsync);

        WeakReferenceMessenger.Default.Register<OccasionListViewModel, OccasionsChangedMessage>(this, (vm, _) => vm.ReloadIfLoaded());
        WeakReferenceMessenger.Default.Register<OccasionListViewModel, CategoriesChangedMessage>(this, (vm, _) => vm.ReloadIfLoaded());
    }

    /// <summary>All occasions grouped by category; pinned first within a group, then alphabetical (Windows list pane).</summary>
    public ObservableCollection<OccasionGroup> OccasionGroups { get; } = new();

    /// <summary>The same groups without the pinned occasion, which Android shows as the tonal hero instead.</summary>
    public ObservableCollection<OccasionGroup> UnpinnedGroups { get; } = new();

    public List<OccasionSummary> AllSummaries { get; private set; } = [];

    public OccasionSummary? FeaturedSummary
    {
        get => _featuredSummary;
        private set
        {
            if (SetProperty(ref _featuredSummary, value))
            {
                OnPropertyChanged(nameof(HasFeaturedOccasion));
                UpdateBreakdown();
            }
        }
    }

    public bool HasFeaturedOccasion => FeaturedSummary is not null;
    public bool HasOccasions => AllSummaries.Count > 0;
    public bool IsEmpty => _hasLoaded && AllSummaries.Count == 0;

    public string FeaturedBreakdown
    {
        get => _featuredBreakdown;
        private set => SetProperty(ref _featuredBreakdown, value);
    }

    /// <summary>"4 occasions · 4 categories" under the desktop header.</summary>
    public string HomeSubtitle
    {
        get => _homeSubtitle;
        private set => SetProperty(ref _homeSubtitle, value);
    }

    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<OccasionSummary?> OpenDetailCommand { get; }

    public event EventHandler? Loaded;

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;

            var occasions = await _occasionService.GetAllAsync();
            var categoryCount = (await _categoryService.GetAllAsync()).Count;
            var summaries = occasions
                .Select(o => new OccasionSummary(o, _occasionService.GetDays(o)))
                .OrderByDescending(s => s.IsPinned)
                .ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            AllSummaries = summaries;
            FeaturedSummary = summaries.FirstOrDefault(s => s.IsPinned);

            Fill(OccasionGroups, summaries);
            Fill(UnpinnedGroups, summaries.Where(s => !s.IsPinned));

            HomeSubtitle = summaries.Count == 0
                ? "Nothing tracked yet"
                : $"{Plural(summaries.Count, "occasion")} · {Plural(categoryCount, "category", "categories")}";

            _hasLoaded = true;
            OnPropertyChanged(nameof(HasOccasions));
            OnPropertyChanged(nameof(IsEmpty));
            Loaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public OccasionSummary? FindSummary(int id) => AllSummaries.FirstOrDefault(s => s.Id == id);

    protected override void OnLanguageChanged() => ReloadIfLoaded();

    /// <summary>Starts the once-per-second breakdown on the featured card.</summary>
    public void StartTimer()
    {
        if (_timer is not null || Application.Current is null)
            return;

        _timer = Application.Current.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateBreakdown();
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void UpdateBreakdown() =>
        FeaturedBreakdown = FeaturedSummary is null ? string.Empty : OccasionMath.FormatBreakdown(FeaturedSummary.Occasion, DateTime.Now);

    private void ReloadIfLoaded()
    {
        if (_hasLoaded)
            Application.Current?.Dispatcher.Dispatch(async () => await LoadAsync());
    }

    private static void Fill(ObservableCollection<OccasionGroup> target, IEnumerable<OccasionSummary> summaries)
    {
        target.Clear();
        var groups = summaries
            .GroupBy(s => s.CategoryName)
            .OrderBy(g => g.Key == OccasionGroup.UncategorisedName)
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in groups)
            target.Add(new OccasionGroup(group.Key, group));
    }

    private static string Plural(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";

    private Task AddAsync() => _shellService.GoToAsync(nameof(OccasionFormPage));

    private Task OpenDetailAsync(OccasionSummary? summary) =>
        summary is null ? Task.CompletedTask : _shellService.GoToAsync($"{nameof(OccasionDetailPage)}?id={summary.Id}");
}
