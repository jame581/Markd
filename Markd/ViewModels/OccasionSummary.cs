using CommunityToolkit.Mvvm.ComponentModel;
using Markd.Core.Domain;
using Markd.Core.Localization;

namespace Markd.ViewModels;

/// <summary>
/// Precomputed row for Home lists: the occasion plus its count, wording and next milestone,
/// so item templates never call services. <see cref="IsSelected"/> drives the desktop list pane.
/// </summary>
public sealed class OccasionSummary : ObservableObject
{
    private bool _isSelected;

    public OccasionSummary(Occasion occasion, int days)
    {
        Occasion = occasion;
        Days = days;
        Next = OccasionMath.GetNextMilestone(occasion, days);
    }

    public Occasion Occasion { get; }
    public int Days { get; }
    public NextMilestoneInfo? Next { get; }

    public int Id => Occasion.Id;
    public string Title => Occasion.Title;
    public string? Emoji => Occasion.Emoji;
    public string? ColorHex => Occasion.ColorHex;
    public bool IsPinned => Occasion.IsPinned;
    public string? Notes => Occasion.Notes;
    public string CategoryName => Occasion.Category?.Name ?? OccasionGroup.UncategorisedName;

    public int DisplayDays => Math.Abs(Days);
    public string UnitLabel => OccasionMath.UnitLabel(Occasion, Days);
    public string AnchorLabel => OccasionMath.AnchorPhrase(Occasion);
    public string DirectionLabel => OccasionMath.DirectionLabel(Occasion);

    public bool HasNext => Next is not null;
    public string NextHeading => Next is null ? Strings.Occasion_NoMilestoneAhead : string.Format(LocalizationManager.Instance.Culture, Strings.Occasion_NextMilestone, Next.Label);
    public string NextStatus => Next?.DaysToGoText ?? "—";
    public double NextProgress => Next?.Progress ?? 1;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class OccasionGroup(string categoryName, IEnumerable<OccasionSummary> items) : List<OccasionSummary>(items)
{
    public static string UncategorisedName => Strings.Occasion_Uncategorised;

    public string CategoryName { get; } = categoryName;
    public string CountLabel => Plural.Format("Occasion_Count", Count);
}
