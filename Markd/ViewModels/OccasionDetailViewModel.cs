using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;

namespace Markd.ViewModels;

public class OccasionDetailViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private Occasion? _currentOccasion;
    private int _days;
    private string _newMilestoneLabel = string.Empty;
    private string _newMilestoneThresholdDays = string.Empty;

    public OccasionDetailViewModel(IOccasionService occasionService)
    {
        _occasionService = occasionService;
        EditCommand = new AsyncRelayCommand(EditAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        PinCommand = new AsyncRelayCommand(PinAsync);
        AddMilestoneCommand = new AsyncRelayCommand(AddMilestoneAsync);
        RemoveMilestoneCommand = new AsyncRelayCommand<Milestone?>(RemoveMilestoneAsync);
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

    public ObservableCollection<Milestone> Milestones { get; } = new();

    public string NewMilestoneLabel
    {
        get => _newMilestoneLabel;
        set => SetProperty(ref _newMilestoneLabel, value);
    }

    public string NewMilestoneThresholdDays
    {
        get => _newMilestoneThresholdDays;
        set => SetProperty(ref _newMilestoneThresholdDays, value);
    }

    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand PinCommand { get; }
    public IAsyncRelayCommand AddMilestoneCommand { get; }
    public IAsyncRelayCommand<Milestone?> RemoveMilestoneCommand { get; }

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

        Milestones.Clear();
        foreach (var milestone in model.Milestones.OrderBy(m => m.ThresholdDays))
            Milestones.Add(milestone);

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

    private async Task AddMilestoneAsync()
    {
        if (CurrentOccasion == null)
            return;

        if (string.IsNullOrWhiteSpace(NewMilestoneLabel))
        {
            ErrorMessage = "Milestone label is required.";
            return;
        }

        if (!int.TryParse(NewMilestoneThresholdDays, out var thresholdDays) || thresholdDays <= 0)
        {
            ErrorMessage = "Milestone threshold must be a positive number.";
            return;
        }

        await _occasionService.AddMilestoneAsync(CurrentOccasion.Id, thresholdDays, NewMilestoneLabel.Trim());

        NewMilestoneLabel = string.Empty;
        NewMilestoneThresholdDays = string.Empty;
        await LoadAsync(CurrentOccasion.Id);
    }

    private async Task RemoveMilestoneAsync(Milestone? milestone)
    {
        if (CurrentOccasion == null || milestone == null)
            return;

        var confirmed = await Shell.Current.DisplayAlertAsync("Remove milestone", $"Remove '{milestone.Label}'?", "Remove", "Cancel");
        if (!confirmed)
            return;

        await _occasionService.RemoveMilestoneAsync(milestone.Id);
        await LoadAsync(CurrentOccasion.Id);
    }
}
