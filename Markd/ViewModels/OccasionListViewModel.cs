using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class OccasionListViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private Occasion? _featuredOccasion;

    public OccasionListViewModel(IOccasionService occasionService)
    {
        _occasionService = occasionService;
        AddCommand = new AsyncRelayCommand(AddAsync);
        OpenDetailCommand = new AsyncRelayCommand<OccasionSummary?>(OpenDetailAsync);
        ManageCategoriesCommand = new AsyncRelayCommand(ManageCategoriesAsync);
    }

    public ObservableCollection<OccasionGroup> OccasionGroups { get; } = new();

    public Occasion? FeaturedOccasion
    {
        get => _featuredOccasion;
        private set => SetProperty(ref _featuredOccasion, value);
    }

    public bool HasFeaturedOccasion => FeaturedOccasion is not null;

    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<OccasionSummary?> OpenDetailCommand { get; }
    public IAsyncRelayCommand ManageCategoriesCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            OccasionGroups.Clear();

            var occasions = await _occasionService.GetAllAsync();

            FeaturedOccasion = occasions.FirstOrDefault(o => o.IsPinned);
            OnPropertyChanged(nameof(HasFeaturedOccasion));

            var summaries = occasions
                .Select(o => new OccasionSummary(o, _occasionService.GetDays(o)))
                .OrderByDescending(s => s.Occasion.IsPinned)
                .ThenBy(s => s.Occasion.Title);

            var grouped = summaries
                .GroupBy(s => string.IsNullOrWhiteSpace(s.Occasion.Category?.Name)
                    ? "Uncategorized" : s.Occasion.Category!.Name)
                .OrderBy(g => g.Key)
                .Select(g => new OccasionGroup(g.Key, g));

            foreach (var group in grouped)
                OccasionGroups.Add(group);
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

    private async Task AddAsync()
    {
        await Shell.Current.GoToAsync(nameof(OccasionFormPage));
    }

    private async Task OpenDetailAsync(OccasionSummary? summary)
    {
        if (summary == null)
            return;

        await Shell.Current.GoToAsync($"{nameof(OccasionDetailPage)}?id={summary.Occasion.Id}");
    }

    private async Task ManageCategoriesAsync()
    {
        await Shell.Current.GoToAsync("//categories");
    }
}
