namespace Markd.Core.Domain
{
    /// <summary>
    /// Date math for occasions. Anchors are stored as UTC instants of a local midnight;
    /// counts are always taken between local calendar dates so they change at local midnight.
    /// </summary>
    public static class OccasionDates
    {
        /// <summary>
        /// Converts a stored anchor (UTC, or Unspecified as read back from SQLite) to its local calendar date.
        /// </summary>
        public static DateTime ToLocalDate(DateTime storedAnchor, TimeZoneInfo? zone = null)
        {
            var utc = storedAnchor.Kind == DateTimeKind.Local
                ? storedAnchor.ToUniversalTime()
                : DateTime.SpecifyKind(storedAnchor, DateTimeKind.Utc);

            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone ?? TimeZoneInfo.Local).Date;
        }

        /// <summary>
        /// Elapsed days for Since, remaining days for Until (negative once an Until date has passed).
        /// </summary>
        public static int GetDays(Occasion occasion, DateTime todayLocal, TimeZoneInfo? zone = null)
        {
            var anchor = ToLocalDate(occasion.AnchorDate, zone);
            var today = todayLocal.Date;

            return occasion.Direction switch
            {
                OccasionDirection.Since => (today - anchor).Days,
                OccasionDirection.Until => (anchor - today).Days,
                _ => 0
            };
        }

        /// <summary>
        /// The local date a milestone lands on: anchor + threshold for Since, anchor − threshold for Until.
        /// </summary>
        public static DateTime GetMilestoneDate(Occasion occasion, Milestone milestone, TimeZoneInfo? zone = null)
        {
            var anchor = ToLocalDate(occasion.AnchorDate, zone);
            return occasion.Direction == OccasionDirection.Since
                ? anchor.AddDays(milestone.ThresholdDays)
                : anchor.AddDays(-milestone.ThresholdDays);
        }

        /// <summary>
        /// True once the milestone threshold has been crossed for the given day count.
        /// </summary>
        public static bool IsMilestoneReached(Occasion occasion, Milestone milestone, int days) =>
            occasion.Direction == OccasionDirection.Since
                ? days >= milestone.ThresholdDays
                : days <= milestone.ThresholdDays;
    }
}
