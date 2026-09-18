using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;
using Microsoft.Maui.Devices;

namespace Markd.ViewModels;

public class OccasionDetailViewModel : ViewModelBase
{
    private readonly IOccasionService _occasionService;
    private readonly IAppShellService _shellService;
    private readonly IMilestoneEditorService _milestoneEditorService;
    private readonly IShareService _shareService;
    private readonly IFeedbackService _feedbackService;
    private Occasion? _currentOccasion;
    private int _days;
    private string _timeBreakdown = string.Empty;
    private NextMilestoneInfo? _next;
    private IDispatcherTimer? _timer;

    public OccasionDetailViewModel(
        IOccasionService occasionService,
        IAppShellService shellService,
        IMilestoneEditorService milestoneEditorService,
        IShareService shareService,
        IFeedbackService feedbackService)
    {
        _occasionService = occasionService;
        _shellService = shellService;
        _milestoneEditorService = milestoneEditorService;
        _shareService = shareService;
        _feedbackService = feedbackService;
        EditCommand = new AsyncRelayCommand(EditAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        PinCommand = new AsyncRelayCommand(PinAsync);
        AddMilestoneCommand = new AsyncRelayCommand(AddMilestoneAsync);
        RemoveMilestoneCommand = new AsyncRelayCommand<Milestone?>(RemoveMilestoneAsync);
        ShareCommand = new AsyncRelayCommand(ShareAsync);
        ShareMilestoneCommand = new AsyncRelayCommand<Milestone?>(ShareMilestoneAsync);

        WeakReferenceMessenger.Default.Register<OccasionDetailViewModel, OccasionsChangedMessage>(this, (vm, message) => vm.OnOccasionsChanged(message));
        WeakReferenceMessenger.Default.Register<OccasionDetailViewModel, CategoriesChangedMessage>(this, (vm, _) => vm.OnOccasionsChanged(new OccasionsChangedMessage()));
    }

    /// <summary>
    /// The loaded occasion. The long-lived DbContext hands back the same tracked instance on every load,
    /// so the derived properties are raised on every assignment, not only when the reference changes.
    /// </summary>
    public Occasion? CurrentOccasion
    {
        get => _currentOccasion;
        private set
        {
            _currentOccasion = value;
            OnPropertyChanged();
            RaiseOccasionProperties();
        }
    }

    public bool HasOccasion => CurrentOccasion is not null;
    public string Title => CurrentOccasion?.Title ?? string.Empty;
    public string? Emoji => CurrentOccasion?.Emoji;
    public string? ColorHex => CurrentOccasion?.ColorHex;
    public bool IsPinned => CurrentOccasion?.IsPinned == true;
    public string CategoryName => CurrentOccasion?.Category?.Name ?? OccasionGroup.UncategorisedName;
    public DateTime? LocalAnchorDate => CurrentOccasion is null ? null : OccasionDates.ToLocalDate(CurrentOccasion.AnchorDate);
    public string AnchorShort => LocalAnchorDate is { } date ? OccasionMath.FormatShortDate(date) : string.Empty;
    public string AnchorLong => CurrentOccasion is null || LocalAnchorDate is not { } date
        ? string.Empty
        : $"{OccasionMath.DirectionLabel(CurrentOccasion)} {OccasionMath.FormatLongDate(date)}";
    public string DirectionLabel => CurrentOccasion is null ? string.Empty : OccasionMath.DirectionLabel(CurrentOccasion);
    public string DirectionChipText => DirectionLabel.ToUpperInvariant();
    public string NotesText => string.IsNullOrWhiteSpace(CurrentOccasion?.Notes) ? Strings.Detail_NoNotes : CurrentOccasion!.Notes!.Trim();
    public bool HasNotes => !string.IsNullOrWhiteSpace(CurrentOccasion?.Notes);
    public string PinButtonText => IsPinned ? Strings.Detail_UnpinAction : Strings.Detail_PinAction;
    public string PinToHomeText => IsPinned ? Strings.Detail_UnpinAction : Strings.Form_PinToHome;

    public int Days
    {
        get => _days;
        private set
        {
            if (SetProperty(ref _days, value))
            {
                OnPropertyChanged(nameof(DisplayDays));
                OnPropertyChanged(nameof(UnitLabel));
            }
        }
    }

    public int DisplayDays => Math.Abs(Days);
    public string UnitLabel => CurrentOccasion is null
        ? Strings.ResourceManager.GetString("Occasion_UnitDays_Many", LocalizationManager.Instance.Culture) ?? "Occasion_UnitDays_Many"
        : OccasionMath.UnitLabel(CurrentOccasion, Days);

    /// <summary>Live breakdown, e.g. "3y 11mo 4d 08h 41min 09s", refreshed once per second while the timer runs.</summary>
    public string TimeBreakdown
    {
        get => _timeBreakdown;
        private set => SetProperty(ref _timeBreakdown, value);
    }

    public ObservableCollection<MilestoneViewState> MilestoneStates { get; } = new();
    public bool HasMilestones => MilestoneStates.Count > 0;
    public string MilestoneCountText => Plural.Format("Detail_MilestoneCount", MilestoneStates.Count);

    public NextMilestoneInfo? NextMilestone
    {
        get => _next;
        private set
        {
            if (SetProperty(ref _next, value))
            {
                OnPropertyChanged(nameof(HasNextMilestone));
                OnPropertyChanged(nameof(NextMilestoneHeading));
                OnPropertyChanged(nameof(NextMilestoneStatus));
                OnPropertyChanged(nameof(NextMilestoneShort));
                OnPropertyChanged(nameof(NextMilestoneProgress));
            }
        }
    }

    public bool HasNextMilestone => NextMilestone is not null;
    public string NextMilestoneHeading => NextMilestone is null ? Strings.Occasion_NoMilestoneAhead : string.Format(Strings.Occasion_NextMilestone, NextMilestone.Label);
    public string NextMilestoneStatus => NextMilestone?.DaysToGoText ?? "—";
    public string NextMilestoneShort => NextMilestone is null ? "—" : Plural.Format("Unit_Days", NextMilestone.DaysAway);
    public double NextMilestoneProgress => NextMilestone?.Progress ?? 1;

    public IAsyncRelayCommand EditCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand PinCommand { get; }
    public IAsyncRelayCommand AddMilestoneCommand { get; }
    public IAsyncRelayCommand<Milestone?> RemoveMilestoneCommand { get; }
    public IAsyncRelayCommand ShareCommand { get; }
    public IAsyncRelayCommand<Milestone?> ShareMilestoneCommand { get; }

    public async Task LoadAsync(int id)
    {
        var model = await _occasionService.GetByIdAsync(id);
        if (model == null)
        {
            CurrentOccasion = null;
            ErrorMessage = Strings.Occasion_NotFound;
            return;
        }

        Days = _occasionService.GetDays(model);
        CurrentOccasion = model;
        UpdateTimeBreakdown();

        MilestoneStates.Clear();
        foreach (var milestone in model.Milestones.OrderBy(m => m.ThresholdDays))
            MilestoneStates.Add(CreateMilestoneState(model, milestone));

        NextMilestone = OccasionMath.GetNextMilestone(model, Days);
        OnPropertyChanged(nameof(HasMilestones));
        OnPropertyChanged(nameof(MilestoneCountText));
        ErrorMessage = null;
    }

    public void Clear()
    {
        CurrentOccasion = null;
        MilestoneStates.Clear();
        NextMilestone = null;
        TimeBreakdown = string.Empty;
        OnPropertyChanged(nameof(HasMilestones));
    }

    /// <summary>Starts the once-per-second breakdown; call when the view appears.</summary>
    public void StartTimer()
    {
        if (_timer is not null || Application.Current is null)
            return;

        _timer = Application.Current.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateTimeBreakdown();
        _timer.Start();
    }

    public void StopTimer()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void UpdateTimeBreakdown() =>
        TimeBreakdown = CurrentOccasion is null ? string.Empty : OccasionMath.FormatBreakdown(CurrentOccasion, DateTime.Now);

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

        var occasion = CurrentOccasion;
        var android = _shellService.Platform == DevicePlatform.Android;

        // Android deletes are reversible rather than confirmed; everywhere else the delete is confirmed first.
        if (!android)
        {
            var milestones = occasion.Milestones.Count;
            var confirmed = await _shellService.DisplayAlertAsync(
                string.Format(Strings.Detail_DeleteConfirmTitle, occasion.Title),
                Plural.Format("Detail_DeleteConfirmMessage", milestones),
                Strings.Common_Delete,
                Strings.Common_Cancel,
                destructive: true);
            if (!confirmed)
                return;
        }

        var snapshot = await _occasionService.GetByIdAsync(occasion.Id) ?? occasion;
        // The timer belongs to the view: the phone page stops it when it disappears, the desktop pane keeps it for the next selection.
        await _occasionService.DeleteAsync(occasion.Id);
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(occasion.Id, this));
        await _shellService.GoToAsync("..");

        if (android)
        {
            if (await _feedbackService.ShowUndoAsync(string.Format(Strings.Detail_OccasionDeletedUndo, occasion.Title)))
            {
                var restored = await _occasionService.RestoreAsync(snapshot);
                WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(restored.Id, this));
            }
        }
        else
        {
            await _feedbackService.ShowAsync(Strings.Detail_OccasionDeletedTitle, string.Format(Strings.Detail_OccasionDeletedDetail, occasion.Title));
        }
    }

    private async Task PinAsync()
    {
        if (CurrentOccasion == null)
            return;

        var occasion = CurrentOccasion;
        var wasPinned = occasion.IsPinned;
        if (wasPinned)
        {
            occasion.IsPinned = false;
            await _occasionService.UpdateAsync(occasion);
        }
        else
        {
            await _occasionService.SetPinnedAsync(occasion.Id);
        }

        await LoadAsync(occasion.Id);
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(occasion.Id, this));

        if (_shellService.Platform != DevicePlatform.Android)
        {
            await _feedbackService.ShowAsync(
                wasPinned ? Strings.Detail_UnpinnedTitle : Strings.Detail_PinnedTitle,
                wasPinned ? string.Format(Strings.Detail_UnpinnedDetail, occasion.Title) : string.Format(Strings.Detail_PinnedDetail, occasion.Title));
        }
    }

    private async Task AddMilestoneAsync()
    {
        if (CurrentOccasion == null)
            return;

        var occasion = CurrentOccasion;
        var result = await _milestoneEditorService.PromptAsync();
        if (result is null)
            return;

        var milestone = await _occasionService.AddMilestoneAsync(occasion.Id, result.ThresholdDays, result.Label);
        var alreadyReached = OccasionDates.IsMilestoneReached(occasion, milestone, Days);
        if (alreadyReached)
            await _occasionService.MarkMilestoneNotifiedAsync(milestone.Id);

        await LoadAsync(occasion.Id);
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(occasion.Id, this));

        if (_shellService.Platform != DevicePlatform.Android)
        {
            await _feedbackService.ShowAsync(
                Strings.Detail_MilestoneAddedTitle,
                alreadyReached
                    ? string.Format(Strings.Detail_MilestoneAlreadyPassed, occasion.Title, result.Label)
                    : string.Format(Strings.Detail_MilestoneAtDays, result.Label, result.ThresholdDays));
        }
    }

    private async Task RemoveMilestoneAsync(Milestone? milestone)
    {
        if (CurrentOccasion == null || milestone == null)
            return;

        var occasion = CurrentOccasion;
        var platform = _shellService.Platform;

        // iOS confirms; Android offers undo; the desktop removes on the hover affordance and says so.
        if (platform != DevicePlatform.Android && platform != DevicePlatform.WinUI)
        {
            var confirmed = await _shellService.DisplayAlertAsync(
                Strings.Detail_RemoveMilestone,
                string.Format(Strings.Detail_RemoveMilestoneConfirm, milestone.Label),
                Strings.Common_Remove,
                Strings.Common_Cancel,
                destructive: true);
            if (!confirmed)
                return;
        }

        await _occasionService.RemoveMilestoneAsync(milestone.Id);
        await LoadAsync(occasion.Id);
        WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(occasion.Id, this));

        if (platform == DevicePlatform.Android)
        {
            if (await _feedbackService.ShowUndoAsync(string.Format(Strings.Detail_MilestoneRemovedUndo, milestone.Label)))
            {
                var restored = await _occasionService.AddMilestoneAsync(occasion.Id, milestone.ThresholdDays, milestone.Label);
                if (milestone.Notified)
                    await _occasionService.MarkMilestoneNotifiedAsync(restored.Id);

                if (CurrentOccasion?.Id == occasion.Id)
                    await LoadAsync(occasion.Id);
                WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(occasion.Id, this));
            }
        }
        else if (platform == DevicePlatform.WinUI)
        {
            await _feedbackService.ShowAsync(Strings.Detail_MilestoneRemovedTitle, string.Format(Strings.Detail_MilestoneRemovedDetail, milestone.Label));
        }
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
            NextMilestone?.DaysToGoText));
    }

    private async Task ShareMilestoneAsync(Milestone? milestone)
    {
        if (CurrentOccasion is null || milestone is null)
            return;

        await _shareService.ShareMilestoneAsync(new MilestoneShareRequest(
            CurrentOccasion,
            milestone.Label,
            milestone.ThresholdDays,
            NextMilestone?.Label));
    }

    protected override void OnLanguageChanged() => OnOccasionsChanged(new OccasionsChangedMessage());

    private void OnOccasionsChanged(OccasionsChangedMessage message)
    {
        if (ReferenceEquals(message.Source, this) || CurrentOccasion is null || (message.OccasionId is { } id && id != CurrentOccasion.Id))
            return;

        var currentId = CurrentOccasion.Id;
        Application.Current?.Dispatcher.Dispatch(async () =>
        {
            if (CurrentOccasion?.Id == currentId && await _occasionService.GetByIdAsync(currentId) is not null)
                await LoadAsync(currentId);
        });
    }

    private MilestoneViewState CreateMilestoneState(Occasion occasion, Milestone milestone)
    {
        var reached = milestone.Notified || OccasionDates.IsMilestoneReached(occasion, milestone, Days);
        var daysAway = occasion.Direction == OccasionDirection.Since
            ? milestone.ThresholdDays - Days
            : Days - milestone.ThresholdDays;

        var statusText = reached
            ? string.Format(Strings.Detail_MilestoneReachedOn, OccasionMath.FormatShortDate(OccasionDates.GetMilestoneDate(occasion, milestone)))
            : OccasionMath.FormatDaysAway(daysAway);

        return new MilestoneViewState(milestone, reached, statusText, daysAway);
    }

    private void RaiseOccasionProperties()
    {
        foreach (var name in new[]
                 {
                     nameof(HasOccasion), nameof(Title), nameof(Emoji), nameof(ColorHex), nameof(IsPinned), nameof(CategoryName),
                     nameof(LocalAnchorDate), nameof(AnchorShort), nameof(AnchorLong), nameof(DirectionLabel), nameof(DirectionChipText),
                     nameof(NotesText), nameof(HasNotes), nameof(PinButtonText), nameof(PinToHomeText), nameof(UnitLabel)
                 })
        {
            OnPropertyChanged(name);
        }
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
    public string ThresholdText => $"{Milestone.ThresholdDays}{Strings.Abbr_Day}";
    public string StatusText { get; }
    public int DaysAway { get; }
    public bool IsReached { get; }
    public bool IsUpcoming => !IsReached;
}
