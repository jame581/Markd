using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class OccasionDetailViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private Occasion? _currentOccasion;
    private int _days;

    public OccasionDetailViewModel(IOccasionService occasionService)
    {
        _occasionService = occasionService;
        EditCommand = new AsyncRelayCommand(EditAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        PinCommand = new AsyncRelayCommand(PinAsync);
    }

    public Occasion? CurrentOccasion
    {
        get => _currentOccasion;
        set => SetProperty(ref _currentOccasion, value);
    }

    public int Days
    {
        get => _days;
        set => SetProperty(ref _days, value);
    }

    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand PinCommand { get; }

    public async Task LoadAsync(int id)
    {
        var model = await _occasionService.GetByIdAsync(id);
        if (model == null)
        {
            ErrorMessage = "Occasion not found.";
            return;
        }

        CurrentOccasion = model;
        Days = _occasionService.GetDays(model);
        ErrorMessage = null;
    }

    private async Task EditAsync()
    {
        if (CurrentOccasion == null)
            return;

        await Shell.Current.GoToAsync($"{nameof(OccasionFormPage)}?id={CurrentOccasion.Id}");
    }

    private async Task DeleteAsync()
    {
        if (CurrentOccasion == null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync("Delete", $"Delete '{CurrentOccasion.Title}'?", "Delete", "Cancel");
        if (!confirmed)
            return;

        await _occasionService.DeleteAsync(CurrentOccasion.Id);
        await Shell.Current.GoToAsync("..");
    }

    private async Task PinAsync()
    {
        if (CurrentOccasion == null)
            return;

        await _occasionService.SetPinnedAsync(CurrentOccasion.Id);
        await LoadAsync(CurrentOccasion.Id);
    }
}
