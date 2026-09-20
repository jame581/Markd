using Markd.Core.Domain;

namespace Markd.Services
{
    /// <summary>One upcoming milestone and the local instant its alert is due.</summary>
    public sealed record ScheduledMilestone(Occasion Occasion, Milestone Milestone, DateTime When);

    /// <summary>
    /// Which milestones get a system alert, and when. Platform-free on purpose: every platform
    /// schedules the same set, and this is the part worth testing.
    /// </summary>
    public static class MilestoneSchedule
    {
        public static IReadOnlyList<ScheduledMilestone> Upcoming(
            IEnumerable<Occasion> occasions, AppSettings settings, DateTime now, int max)
        {
            if (!settings.NotificationsEnabled)
                return [];

            return occasions
                .SelectMany(o => o.Milestones
                    .Where(m => !m.Notified)
                    .Select(m => new ScheduledMilestone(
                        o, m, OccasionDates.GetMilestoneDate(o, m) + settings.NotificationTimeOfDay)))
                .Where(x => x.When > now)
                .OrderBy(x => x.When)
                .Take(max)
                .ToList();
        }
    }
}
