using System.Globalization;
using Markd.Core.Domain;

namespace Markd.ViewModels;

/// <summary>Next unreached milestone and progress towards it (0.04–1).</summary>
public sealed record NextMilestoneInfo(Milestone Milestone, int DaysAway, double Progress)
{
    public string Label => Milestone.Label;
    public string DaysAwayText => OccasionMath.FormatDaysAway(DaysAway);
    public string DaysToGoText => DaysAway == 1 ? "1 day to go" : $"{DaysAway} days to go";
    public string ShortText => $"{DaysAway}d";
}

/// <summary>
/// Display math shared by every screen: the live breakdown, next milestone, units and date wording.
/// </summary>
public static class OccasionMath
{
    private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Calendar-accurate "3y 10mo 14d 08h 41min 09s", dropping leading zero units.
    /// Since counts from the anchor to now; Until counts from now to the anchor (prefixed "-" once passed).
    /// </summary>
    public static string FormatBreakdown(Occasion occasion, DateTime nowLocal, TimeZoneInfo? zone = null)
    {
        var anchor = OccasionDates.ToLocalDate(occasion.AnchorDate, zone);
        var (from, to) = occasion.Direction == OccasionDirection.Since ? (anchor, nowLocal) : (nowLocal, anchor);
        var prefix = string.Empty;
        if (to < from)
        {
            // An Until occasion reads zero for the whole of its day rather than "-0d …".
            if (occasion.Direction == OccasionDirection.Until && anchor.Date == nowLocal.Date)
                return "0d 00h 00min 00s";

            (from, to) = (to, from);
            prefix = occasion.Direction == OccasionDirection.Until ? "-" : string.Empty;
        }

        var years = 0;
        while (from.AddYears(years + 1) <= to) years++;
        var cursor = from.AddYears(years);

        var months = 0;
        while (cursor.AddMonths(months + 1) <= to) months++;
        cursor = cursor.AddMonths(months);

        var days = 0;
        while (cursor.AddDays(days + 1) <= to) days++;
        cursor = cursor.AddDays(days);

        var rest = to - cursor;
        var clock = $"{rest.Hours:D2}h {rest.Minutes:D2}min {rest.Seconds:D2}s";

        if (years > 0) return $"{prefix}{years}y {months}mo {days}d {clock}";
        if (months > 0) return $"{prefix}{months}mo {days}d {clock}";
        return $"{prefix}{days}d {clock}";
    }

    /// <summary>
    /// Next milestone not yet reached, with progress measured from the previous milestone
    /// (or the anchor / creation date when there is none), clamped to 4–100%.
    /// </summary>
    public static NextMilestoneInfo? GetNextMilestone(Occasion occasion, int days, TimeZoneInfo? zone = null)
    {
        if (occasion.Direction == OccasionDirection.Since)
        {
            var sorted = occasion.Milestones.OrderBy(m => m.ThresholdDays).ToList();
            var next = sorted.FirstOrDefault(m => m.ThresholdDays > days);
            if (next is null)
                return null;

            var previous = sorted.LastOrDefault(m => m.ThresholdDays <= days)?.ThresholdDays ?? 0;
            return new NextMilestoneInfo(next, next.ThresholdDays - days, Progress(days - previous, next.ThresholdDays - previous));
        }

        // Until: thresholds count days remaining, so the next one is the largest threshold still below the remaining count.
        var descending = occasion.Milestones.OrderByDescending(m => m.ThresholdDays).ToList();
        var upcoming = descending.FirstOrDefault(m => m.ThresholdDays < days);
        if (upcoming is null)
            return null;

        var passed = descending.LastOrDefault(m => m.ThresholdDays >= days)?.ThresholdDays;
        var startRemaining = passed ?? Math.Max(days, InitialRemaining(occasion, zone));
        return new NextMilestoneInfo(upcoming, days - upcoming.ThresholdDays, Progress(startRemaining - days, startRemaining - upcoming.ThresholdDays));
    }

    public static string FormatDaysAway(int daysAway) => daysAway switch
    {
        0 => "Today",
        1 => "1 day away",
        > 1 => $"{daysAway} days away",
        _ => $"{Math.Abs(daysAway)} days past"
    };

    public static string FormatShortDate(DateTime date) => date.ToString("d MMM yyyy", DateCulture);

    public static string FormatLongDate(DateTime date) => date.ToString("dddd, d MMMM yyyy", DateCulture);

    /// <summary>"since 14 Oct 2022" / "until 19 Dec 2026".</summary>
    public static string AnchorPhrase(Occasion occasion, TimeZoneInfo? zone = null) =>
        (occasion.Direction == OccasionDirection.Since ? "since " : "until ")
        + FormatShortDate(OccasionDates.ToLocalDate(occasion.AnchorDate, zone));

    /// <summary>Unit under a count: "days"; "to go" for a future Until or a Since that has not started; "days ago" for a passed Until.</summary>
    public static string UnitLabel(Occasion occasion, int days) => occasion.Direction switch
    {
        OccasionDirection.Until when days >= 0 => "to go",
        OccasionDirection.Until => "days ago",
        OccasionDirection.Since when days < 0 => "to go",
        _ => "days"
    };

    public static string DirectionLabel(Occasion occasion) =>
        occasion.Direction == OccasionDirection.Since ? "Since" : "Until";

    private static int InitialRemaining(Occasion occasion, TimeZoneInfo? zone)
    {
        if (occasion.CreatedAt == default)
            return 0;

        var created = OccasionDates.ToLocalDate(occasion.CreatedAt, zone);
        return (OccasionDates.ToLocalDate(occasion.AnchorDate, zone) - created).Days;
    }

    private static double Progress(double done, double span) =>
        span <= 0 ? 1 : Math.Clamp(done / span, 0.04, 1);
}
