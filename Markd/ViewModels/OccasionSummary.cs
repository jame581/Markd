using Markd.Core.Domain;

namespace Markd.ViewModels;

/// <summary>
/// Lightweight DTO used by the dashboard list — holds the occasion plus the pre-computed
/// days value so the item template can display it without an extra service call.
/// </summary>
public record OccasionSummary(Occasion Occasion, int Days)
{
    public DateTime LocalAnchorDate => Occasion.AnchorDate.ToLocalTime().Date;

    public int DisplayDays => Math.Abs(Days);

    public string CountLabel => Occasion.Direction == OccasionDirection.Since
        ? "DAYS"
        : Days >= 0 ? "TO GO" : "AGO";

    public string AnchorLabel => Occasion.Direction == OccasionDirection.Since
        ? $"since {LocalAnchorDate:dd MMM yyyy}"
        : Days >= 0
            ? $"until {LocalAnchorDate:dd MMM yyyy}"
            : $"was {LocalAnchorDate:dd MMM yyyy}";

    /// <summary>
    /// Human-readable time label shown on the list card.
    /// e.g. "365 days since" or "12 days until"
    /// </summary>
    public string TimeLabel => Occasion.Direction == OccasionDirection.Since
        ? $"{Days} days since"
        : Days >= 0 ? $"in {Days} days" : $"{Math.Abs(Days)} days ago";
}
