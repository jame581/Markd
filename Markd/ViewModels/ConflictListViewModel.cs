using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Core.Sync;

namespace Markd.ViewModels;

/// <summary>
/// Lists the records both devices changed and collects one choice per record. The sync coordinator builds it from the
/// conflicts of one merge and awaits <see cref="Result"/>: the resolutions, or null when the user chose to decide later.
/// </summary>
public sealed class ConflictListViewModel : ViewModelBase
{
    private readonly TaskCompletionSource<IReadOnlyList<ConflictResolution>?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConflictListViewModel(IReadOnlyList<SyncConflict> conflicts, Func<DateTime>? utcNow = null)
    {
        var now = utcNow ?? (() => DateTime.UtcNow);
        Rows = new ObservableCollection<ConflictRowViewModel>(conflicts.Select(c => new ConflictRowViewModel(c, now)));
        foreach (var row in Rows)
            row.PropertyChanged += OnRowChanged;

        ChooseAllLocalCommand = new RelayCommand(() => SetAll(_ => ConflictChoice.Local));
        ChooseAllRemoteCommand = new RelayCommand(() => SetAll(_ => ConflictChoice.Remote));
        ChooseAllNewestCommand = new RelayCommand(() => SetAll(r => r.Conflict.Newest));
        ApplyCommand = new RelayCommand(Apply, () => Rows.All(r => r.Choice is not null));
        LaterCommand = new RelayCommand(() => _result.TrySetResult(null));
    }

    public ObservableCollection<ConflictRowViewModel> Rows { get; }

    public string HeaderText => Plural.Format("Sync_ConflictsHeader", Rows.Count, LocalizationManager.Instance.Culture);

    public RelayCommand ChooseAllLocalCommand { get; }

    public RelayCommand ChooseAllRemoteCommand { get; }

    public RelayCommand ChooseAllNewestCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand LaterCommand { get; }

    /// <summary>Completes with one resolution per conflict after Apply, or null after Later.</summary>
    public Task<IReadOnlyList<ConflictResolution>?> Result => _result.Task;

    protected override void OnLanguageChanged()
    {
        foreach (var row in Rows)
            row.Refresh();
        base.OnLanguageChanged();
    }

    private void SetAll(Func<ConflictRowViewModel, ConflictChoice> choose)
    {
        foreach (var row in Rows)
            row.Choice = choose(row);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConflictRowViewModel.Choice))
            ApplyCommand.NotifyCanExecuteChanged();
    }

    private void Apply()
    {
        if (Rows.Any(r => r.Choice is null))
            return;

        _result.TrySetResult(Rows.Select(r => r.Conflict.Resolve(r.Choice!.Value)).ToList());
    }
}

public sealed class ConflictRowViewModel : ObservableObject
{
    private readonly Func<DateTime> _utcNow;
    private ConflictChoice? _choice;

    internal ConflictRowViewModel(SyncConflict conflict, Func<DateTime> utcNow)
    {
        Conflict = conflict;
        _utcNow = utcNow;
        ChooseLocalCommand = new RelayCommand(() => Choice = ConflictChoice.Local);
        ChooseRemoteCommand = new RelayCommand(() => Choice = ConflictChoice.Remote);
        CompareCommand = new RelayCommand(() => CompareRequested?.Invoke(this, BuildCompare()));
    }

    public SyncConflict Conflict { get; }

    /// <summary>Raised when the user asks to compare the two versions; the UI shows the model.</summary>
    public event EventHandler<ConflictCompareViewModel>? CompareRequested;

    public RelayCommand ChooseLocalCommand { get; }

    public RelayCommand ChooseRemoteCommand { get; }

    public RelayCommand CompareCommand { get; }

    public ConflictChoice? Choice
    {
        get => _choice;
        set
        {
            if (SetProperty(ref _choice, value))
            {
                OnPropertyChanged(nameof(IsLocalChosen));
                OnPropertyChanged(nameof(IsRemoteChosen));
            }
        }
    }

    public bool IsLocalChosen => Choice == ConflictChoice.Local;

    public bool IsRemoteChosen => Choice == ConflictChoice.Remote;

    public bool IsLocalNewer => Conflict.Newest == ConflictChoice.Local;

    public bool IsRemoteNewer => Conflict.Newest == ConflictChoice.Remote;

    public string Title => Conflict switch
    {
        OccasionConflict o => (o.Local ?? o.Remote)?.Title ?? string.Empty,
        CategoryConflict c => (c.Local ?? c.Remote)?.Name ?? string.Empty,
        MilestoneConflict m => MilestoneTitle(m),
        _ => string.Empty,
    };

    public string KindText => Conflict.Kind switch
    {
        SyncRecordKind.Occasion => Strings.Sync_KindOccasion,
        SyncRecordKind.Category => Strings.Sync_KindCategory,
        _ => Strings.Sync_KindMilestone,
    };

    public string LocalChipText => Side(ConflictChoice.Local) is null ? Strings.Sync_ThisDeviceDeleted : Strings.Sync_ThisDevice;

    public string RemoteChipText => Side(ConflictChoice.Remote) is null ? Strings.Sync_OtherDeviceDeleted : Strings.Sync_OtherDevice;

    public string NewerText => Strings.Sync_Newer;

    internal void Refresh() => OnPropertyChanged(string.Empty);

    private static string MilestoneTitle(MilestoneConflict m)
    {
        var label = (m.Local ?? m.Remote)?.Label ?? string.Empty;
        return string.IsNullOrEmpty(m.OccasionTitle) ? label : $"{label} · {m.OccasionTitle}";
    }

    private object? Side(ConflictChoice side) => Conflict switch
    {
        OccasionConflict o => side == ConflictChoice.Local ? o.Local : o.Remote,
        CategoryConflict c => side == ConflictChoice.Local ? c.Local : c.Remote,
        MilestoneConflict m => side == ConflictChoice.Local ? m.Local : m.Remote,
        _ => null,
    };

    private ConflictCompareViewModel BuildCompare()
    {
        var culture = LocalizationManager.Instance.Culture;
        return new ConflictCompareViewModel(
            Title,
            LocalChipText,
            RemoteChipText,
            Fields(ConflictChoice.Local, culture),
            Fields(ConflictChoice.Remote, culture));
    }

    private IReadOnlyList<ConflictField> Fields(ConflictChoice side, CultureInfo culture)
    {
        var local = side == ConflictChoice.Local;
        var updated = local ? Conflict.LocalUpdatedAt : Conflict.RemoteUpdatedAt;
        var changed = new ConflictField(Strings.Sync_FieldChanged, ChangedText(updated, culture));

        switch (Conflict)
        {
            case OccasionConflict o when (local ? o.Local : o.Remote) is { } dto:
                return
                [
                    new(Strings.Form_Title, dto.Title),
                    new(Strings.Form_Emoji, OrNone(dto.Emoji)),
                    new(Strings.Sync_FieldDate, OccasionMath.FormatLongDate(dto.AnchorDay.ToDateTime(TimeOnly.MinValue))),
                    new(Strings.Sync_FieldDirection, dto.Direction == "Until" ? Strings.Occasion_Until : Strings.Occasion_Since),
                    new(Strings.Form_Notes, OrNone(dto.Notes)),
                    new(Strings.Form_Category, OrNone(local ? o.LocalCategoryName : o.RemoteCategoryName)),
                    changed,
                ];
            case CategoryConflict c when (local ? c.Local : c.Remote) is { } dto:
                return
                [
                    new(Strings.Category_Name, dto.Name),
                    new(Strings.Form_Emoji, OrNone(dto.Emoji)),
                    changed,
                ];
            case MilestoneConflict m when (local ? m.Local : m.Remote) is { } dto:
                return
                [
                    new(Strings.Milestone_Name, dto.Label),
                    new(Strings.Milestone_Days, dto.ThresholdDays.ToString(culture)),
                    changed,
                ];
            default:
                return [];
        }
    }

    private static string OrNone(string? value) => string.IsNullOrWhiteSpace(value) ? Strings.Sync_ValueNone : value;

    private string ChangedText(DateTime updatedUtc, CultureInfo culture)
    {
        var days = (int)Math.Floor((_utcNow() - updatedUtc).TotalDays);
        return days <= 0 ? Strings.Sync_ChangedToday : Plural.Format("Sync_ChangedDaysAgo", days, culture);
    }
}

/// <summary>One label/value line of the compare view.</summary>
public sealed record ConflictField(string Label, string Value);

/// <summary>The two versions of one conflicting record side by side. An empty column means that device deleted it.</summary>
public sealed record ConflictCompareViewModel(
    string Title,
    string LocalHeader,
    string RemoteHeader,
    IReadOnlyList<ConflictField> Local,
    IReadOnlyList<ConflictField> Remote);
