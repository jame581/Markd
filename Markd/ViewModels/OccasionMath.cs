using Markd.Core.Domain;
using Markd.Core.Localization;

namespace Markd.ViewModels;

/// <summary>Next unreached milestone and progress towards it (0.04–1).</summary>
public sealed record NextMilestoneInfo(Milestone Milestone, int DaysAway, double Progress)
{
    public string Label => Milestone.Label;
    public string DaysAwayText => OccasionMath.FormatDaysAway(DaysAway);
    public string DaysToGoText => Plural.Format("Occasion_DaysToGo", DaysAway);
    public string ShortText => $"{DaysAway}{Strings.Abbr_Day}";
}

/// <summary>
/// Display math shared by every screen: the live breakdown, next milestone, units and date wording.
/// </summary>
public static class OccasionMath
{
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
                return Clock(0, TimeSpan.Zero, string.Empty);

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
        if (years > 0) return $"{prefix}{years}{Strings.Abbr_Year} {months}{Strings.Abbr_Month} {Clock(days, rest, string.Empty)}";
        if (months > 0) return $"{prefix}{months}{Strings.Abbr_Month} {Clock(days, rest, string.Empty)}";
        return Clock(days, rest, prefix);
    }

    private static string Clock(int days, TimeSpan rest, string prefix) =>
        $"{prefix}{days}{Strings.Abbr_Day} {rest.Hours:D2}{Strings.Abbr_Hour} {rest.Minutes:D2}{Strings.Abbr_Minute} {rest.Seconds:D2}{Strings.Abbr_Second}";

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
        0 => Strings.Occasion_Today,
        > 0 => Plural.Format("Occasion_DaysAway", daysAway),
        _ => Plural.Format("Occasion_DaysPast", Math.Abs(daysAway))
    };

    public static string FormatShortDate(DateTime date) => date.ToString(Strings.Format_ShortDate, LocalizationManager.Instance.Culture);

    public static string FormatLongDate(DateTime date) => date.ToString(Strings.Format_LongDate, LocalizationManager.Instance.Culture);

    /// <summary>"since 14 Oct 2022" / "until 19 Dec 2026".</summary>
    public static string AnchorPhrase(Occasion occasion, TimeZoneInfo? zone = null) => string.Format(
        occasion.Direction == OccasionDirection.Since ? Strings.Occasion_SincePhrase : Strings.Occasion_UntilPhrase,
        FormatShortDate(OccasionDates.ToLocalDate(occasion.AnchorDate, zone)));

    /// <summary>Unit under a count: "days"; "to go" for a future Until or a Since that has not started; "days ago" for a passed Until.</summary>
    public static string UnitLabel(Occasion occasion, int days) => occasion.Direction switch
    {
        OccasionDirection.Until when days >= 0 => PluralWord("Occasion_UnitToGo", days),
        OccasionDirection.Until => PluralWord("Occasion_UnitDaysAgo", days),
        OccasionDirection.Since when days < 0 => PluralWord("Occasion_UnitToGo", days),
        _ => PluralWord("Occasion_UnitDays", days)
    };

    public static string DirectionLabel(Occasion occasion) =>
        occasion.Direction == OccasionDirection.Since ? Strings.Occasion_Since : Strings.Occasion_Until;

    private static string PluralWord(string baseKey, long n) =>
        Strings.ResourceManager.GetString($"{baseKey}_{Plural.Select(n)}", LocalizationManager.Instance.Culture) ?? baseKey;

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
