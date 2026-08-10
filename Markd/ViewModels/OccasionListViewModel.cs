using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class OccasionListViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;

    public OccasionListViewModel(IOccasionService occasionService)
    {
        _occasionService = occasionService;
        AddCommand = new AsyncRelayCommand(AddAsync);
        OpenDetailCommand = new AsyncRelayCommand<Occasion?>(OpenDetailAsync);
    }

    public ObservableCollection<Occasion> Occasions { get; } = new();

    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand<Occasion?> OpenDetailCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Occasions.Clear();

            var occasions = await _occasionService.GetAllAsync();
            foreach (var occasion in occasions.OrderByDescending(o => o.IsPinned).ThenBy(o => o.Title))
                Occasions.Add(occasion);
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

    private async Task OpenDetailAsync(Occasion? occasion)
    {
        if (occasion == null)
            return;

        await Shell.Current.GoToAsync($"{nameof(OccasionDetailPage)}?id={occasion.Id}");
    }
}
