using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Domain;
using Markd.Core.Services;
using Markd.Services;
using Microsoft.Maui.Devices;

namespace Markd.ViewModels;

public class OccasionDetailViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private readonly IAppShellService _shellService;
    private readonly IShareService _shareService;
    private Occasion? _currentOccasion;
    private int _days;
    private string _timeBreakdown = string.Empty;
    private string _newMilestoneLabel = string.Empty;
    private string _newMilestoneThresholdDays = string.Empty;
    private MilestoneViewState? _nextMilestone;
    private double _nextMilestoneProgress;
    private IDispatcherTimer? _timer;

    public OccasionDetailViewModel(
        IOccasionService occasionService,
        IAppShellService shellService,
        IShareService shareService)
    {
        _occasionService = occasionService;
        _shellService = shellService;
        _shareService = shareService;
        EditCommand = new AsyncRelayCommand(EditAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        PinCommand = new AsyncRelayCommand(PinAsync);
        AddMilestoneCommand = new AsyncRelayCommand(AddMilestoneAsync);
        RemoveMilestoneCommand = new AsyncRelayCommand<Milestone?>(RemoveMilestoneAsync);
        ShareCommand = new AsyncRelayCommand(ShareAsync);
    }

    public Occasion? CurrentOccasion
    {
        get => _currentOccasion;
        set
        {
            if (SetProperty(ref _currentOccasion, value))
            {
                OnPropertyChanged(nameof(LocalAnchorDate));
                OnPropertyChanged(nameof(DirectionChipText));
                OnPropertyChanged(nameof(NotesText));
                OnPropertyChanged(nameof(PinButtonText));
            }
        }
    }

    public DateTime? LocalAnchorDate => CurrentOccasion?.AnchorDate.ToLocalTime().Date;

    public int Days
    {
        get => _days;
        set => SetProperty(ref _days, value);
    }

    /// <summary>Live-updating breakdown: "2y 3m 15d 04h 22min 10s"</summary>
    public string TimeBreakdown
    {
        get => _timeBreakdown;
        set => SetProperty(ref _timeBreakdown, value);
    }

    public ObservableCollection<Milestone> Milestones { get; } = new();
    public ObservableCollection<MilestoneViewState> MilestoneStates { get; } = new();

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
    public IAsyncRelayCommand ShareCommand { get; }

    public string DirectionChipText => CurrentOccasion?.Direction == OccasionDirection.Until ? "UNTIL" : "SINCE";
    public string NotesText => string.IsNullOrWhiteSpace(CurrentOccasion?.Notes) ? "No notes yet." : CurrentOccasion!.Notes!.Trim();
    public string PinButtonText => CurrentOccasion?.IsPinned == true ? "Unpin" : "Pin";

    public MilestoneViewState? NextMilestone
    {
        get => _nextMilestone;
        private set
        {
            if (SetProperty(ref _nextMilestone, value))
            {
                OnPropertyChanged(nameof(HasNextMilestone));
                OnPropertyChanged(nameof(NextMilestoneHeading));
                OnPropertyChanged(nameof(NextMilestoneStatus));
            }
        }
    }

    public bool HasNextMilestone => NextMilestone is not null;
    public string NextMilestoneHeading => NextMilestone is null ? string.Empty : $"Next · {NextMilestone.Label}";
    public string NextMilestoneStatus => NextMilestone?.StatusText ?? string.Empty;

    public double NextMilestoneProgress
    {
        get => _nextMilestoneProgress;
        private set => SetProperty(ref _nextMilestoneProgress, value);
    }

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
        UpdateTimeBreakdown();

        Milestones.Clear();
        MilestoneStates.Clear();
        foreach (var milestone in model.Milestones.OrderBy(m => m.ThresholdDays))
        {
            Milestones.Add(milestone);
            MilestoneStates.Add(CreateMilestoneState(model, milestone));
        }

        NextMilestone = MilestoneStates
            .Where(state => !state.IsReached)
            .OrderBy(state => state.DaysAway < 0 ? int.MaxValue : state.DaysAway)
            .ThenBy(state => state.Milestone.ThresholdDays)
            .FirstOrDefault();
        NextMilestoneProgress = CalculateNextMilestoneProgress(NextMilestone);

        ErrorMessage = null;
    }

    /// <summary>Called from the page's OnAppearing to start the live counter.</summary>
    public void StartTimer()
    {
        if (_timer is not null) return;

        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateTimeBreakdown();
        _timer.Start();
    }

    /// <summary>Called from the page's OnDisappearing to stop the live counter.</summary>
    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void UpdateTimeBreakdown()
    {
        if (CurrentOccasion is null)
        {
            TimeBreakdown = string.Empty;
            return;
        }

        var anchor = CurrentOccasion.AnchorDate.Date;
        var now = DateTime.Now;

        DateTime from, to;
        string prefix;

        if (CurrentOccasion.Direction == OccasionDirection.Since)
        {
            from = anchor;
            to = now;
            prefix = string.Empty;
        }
        else
        {
            from = now;
            to = anchor;
            prefix = to < from ? "-" : string.Empty;
        }

        if (to < from) (from, to) = (to, from);

        var years = to.Year - from.Year;
        var months = to.Month - from.Month;
        var days = to.Day - from.Day;

        if (days < 0) { months--; days += DateTime.DaysInMonth(to.AddMonths(-1).Year, to.AddMonths(-1).Month); }
        if (months < 0) { years--; months += 12; }

        var timeOnly = to - to.Date + (to.Date - from.Date - TimeSpan.FromDays((to - from).Days - (days + (to.Day < from.Day ? 0 : 0))));
        var exactDiff = to - from;
        var hours = (int)exactDiff.TotalHours % 24;
        var minutes = exactDiff.Minutes;
        var seconds = exactDiff.Seconds;

        // Re-derive hours/minutes/seconds cleanly from remaining time after whole days
        var totalDays = (int)(to - from).TotalDays;
        var remaining = (to - from) - TimeSpan.FromDays(totalDays);
        hours = remaining.Hours;
        minutes = remaining.Minutes;
        seconds = remaining.Seconds;

        TimeBreakdown = years > 0
            ? $"{prefix}{years}y {months}mo {days}d {hours:D2}h {minutes:D2}min {seconds:D2}s"
            : months > 0
            ? $"{prefix}{months}mo {days}d {hours:D2}h {minutes:D2}min {seconds:D2}s"
            : $"{prefix}{days}d {hours:D2}h {minutes:D2}min {seconds:D2}s";
    }

    private async Task EditAsync()
    {
        if (CurrentOccasion == null)
            return;

        await _shellService.GoToAsync($"{nameof(OccasionFormPage)}?id={CurrentOccasion.Id}");
    }

    private async Task DeleteAsync()
    {
        if (CurrentOccasion == null)
            return;

        var confirmed = await _shellService.DisplayAlertAsync("Delete", $"Delete '{CurrentOccasion.Title}'?", "Delete", "Cancel");
        if (!confirmed)
            return;

        await _occasionService.DeleteAsync(CurrentOccasion.Id);
        await _shellService.GoToAsync("..");
    }

    private async Task PinAsync()
    {
        if (CurrentOccasion == null)
            return;

        if (CurrentOccasion.IsPinned)
        {
            CurrentOccasion.IsPinned = false;
            await _occasionService.UpdateAsync(CurrentOccasion);
        }
        else
        {
            await _occasionService.SetPinnedAsync(CurrentOccasion.Id);
        }

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

        if (_shellService.Platform != DevicePlatform.Android)
        {
            var confirmed = await _shellService.DisplayAlertAsync("Remove milestone", $"Remove '{milestone.Label}'?", "Remove", "Cancel");
            if (!confirmed)
                return;
        }

        await _occasionService.RemoveMilestoneAsync(milestone.Id);
        await LoadAsync(CurrentOccasion.Id);
    }

    private async Task ShareAsync()
    {
        if (CurrentOccasion is null)
            return;

        await _shareService.ShareOccasionAsync(new OccasionShareRequest(
            CurrentOccasion,
            Days,
            TimeBreakdown,
            NextMilestone?.Label,
            NextMilestone?.StatusText));
    }

    private MilestoneViewState CreateMilestoneState(Occasion occasion, Milestone milestone)
    {
        var milestoneDate = occasion.Direction == OccasionDirection.Since
            ? occasion.AnchorDate.ToLocalTime().Date.AddDays(milestone.ThresholdDays)
            : occasion.AnchorDate.ToLocalTime().Date.AddDays(-milestone.ThresholdDays);

        var daysAway = occasion.Direction == OccasionDirection.Since
            ? milestone.ThresholdDays - Days
            : Days - milestone.ThresholdDays;

        var statusText = milestone.Notified
            ? $"Reached {milestoneDate:dd MMM yyyy}"
            : daysAway == 0
                ? "Today"
                : daysAway == 1
                    ? "1 day away"
                    : daysAway > 1
                        ? $"{daysAway} days away"
                        : $"{Math.Abs(daysAway)} days past";

        return new MilestoneViewState(milestone, milestone.Notified, statusText, daysAway);
    }

    private double CalculateNextMilestoneProgress(MilestoneViewState? milestone)
    {
        if (CurrentOccasion is null || milestone is null || milestone.Milestone.ThresholdDays <= 0)
            return 0;

        return CurrentOccasion.Direction == OccasionDirection.Since
            ? Math.Clamp((double)Days / milestone.Milestone.ThresholdDays, 0, 1)
            : Days <= 0
                ? 1
                : Math.Clamp((double)milestone.Milestone.ThresholdDays / Days, 0, 1);
    }
}

public sealed class MilestoneViewState
{
    public MilestoneViewState(Milestone milestone, bool isReached, string statusText, int daysAway)
    {
        Milestone = milestone;
        IsReached = isReached;
        StatusText = statusText;
        DaysAway = daysAway;
    }

    public Milestone Milestone { get; }
    public string Label => Milestone.Label;
    public string ThresholdText => $"{Milestone.ThresholdDays}d";
    public string StatusText { get; }
    public int DaysAway { get; }
    public bool IsReached { get; }
    public string StateGlyph => IsReached ? "✓" : "⚑";
}
