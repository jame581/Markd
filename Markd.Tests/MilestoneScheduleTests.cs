using Markd.Core.Domain;
using Markd.Services;
using Xunit;

namespace Markd.Tests;

public class MilestoneScheduleTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 8, 0, 0, DateTimeKind.Local);

    private static AppSettings Settings(bool enabled = true, int hour = 9) => new()
    {
        NotificationsEnabled = enabled,
        NotificationTimeOfDay = new TimeSpan(hour, 0, 0)
    };

    // Anchors are stored as the UTC instant of a local midnight, which is what
    // OccasionFormViewModel.SaveAsync writes and what OccasionDates.ToLocalDate reads back.
    // SpecifyKind(..., Utc) would look equivalent and silently shift the date by one in
    // every timezone west of UTC, so always convert rather than relabel.
    private static Occasion Since(DateTime localAnchor, params Milestone[] milestones) => new()
    {
        Id = 1,
        Title = "Anniversary",
        AnchorDate = localAnchor.ToUniversalTime(),
        Direction = OccasionDirection.Since,
        Milestones = milestones
    };

    private static Milestone At(int id, int thresholdDays, bool notified = false) =>
        new() { Id = id, ThresholdDays = thresholdDays, Label = $"{thresholdDays} days", Notified = notified };

    [Fact]
    public void Schedules_a_future_milestone_at_the_configured_time_of_day()
    {
        // Anchor 998 days ago, so the 1000-day milestone lands in 2 days.
        var occasion = Since(Now.Date.AddDays(-998), At(10, 1000));

        var result = MilestoneSchedule.Upcoming([occasion], Settings(), Now, 48);

        var entry = Assert.Single(result);
        Assert.Equal(10, entry.Milestone.Id);
        Assert.Equal(Now.Date.AddDays(2).AddHours(9), entry.When);
    }

    [Fact]
    public void Skips_a_milestone_whose_time_has_already_passed_today()
    {
        // Lands today at 09:00, but it is 08:00, so it is still ahead.
        var dueToday = Since(Now.Date.AddDays(-1000), At(11, 1000));
        Assert.Single(MilestoneSchedule.Upcoming([dueToday], Settings(), Now, 48));

        // At 10:00 the same milestone is in the past and must be dropped.
        var later = Now.Date.AddHours(10);
        Assert.Empty(MilestoneSchedule.Upcoming([dueToday], Settings(), later, 48));
    }

    [Fact]
    public void Excludes_milestones_already_notified()
    {
        var occasion = Since(Now.Date.AddDays(-998), At(12, 1000, notified: true));

        Assert.Empty(MilestoneSchedule.Upcoming([occasion], Settings(), Now, 48));
    }

    [Fact]
    public void Counts_backwards_for_an_until_occasion()
    {
        var occasion = new Occasion
        {
            Id = 2,
            Title = "Trip",
            AnchorDate = Now.Date.AddDays(30).ToUniversalTime(),
            Direction = OccasionDirection.Until,
            Milestones = [At(13, 10)]
        };

        var entry = Assert.Single(MilestoneSchedule.Upcoming([occasion], Settings(), Now, 48));

        // 10 days remaining is reached 10 days before the anchor, so 20 days from now.
        Assert.Equal(Now.Date.AddDays(20).AddHours(9), entry.When);
    }

    [Fact]
    public void Orders_soonest_first_and_honours_the_cap()
    {
        var occasion = Since(Now.Date.AddDays(-900), At(1, 910), At(2, 905), At(3, 920));

        var result = MilestoneSchedule.Upcoming([occasion], Settings(), Now, 2);

        Assert.Equal([2, 1], result.Select(r => r.Milestone.Id).ToArray());
    }

    [Fact]
    public void Orders_soonest_first_across_multiple_occasions()
    {
        var anniversary = Since(Now.Date.AddDays(-900), At(1, 910), At(2, 920));
        var trip = new Occasion
        {
            Id = 2,
            Title = "Trip",
            AnchorDate = Now.Date.AddDays(30).ToUniversalTime(),
            Direction = OccasionDirection.Until,
            Milestones = [At(3, 15), At(4, 5)]
        };

        var result = MilestoneSchedule.Upcoming([anniversary, trip], Settings(), Now, 48);

        // Anniversary #1 lands in 10 days, trip #3 in 15, anniversary #2 in 20, trip #4 in 25,
        // interleaving the two occasions so the combined order only holds if SelectMany merges them.
        Assert.Equal([1, 3, 2, 4], result.Select(r => r.Milestone.Id).ToArray());
    }

    [Fact]
    public void Returns_nothing_when_notifications_are_disabled()
    {
        var occasion = Since(Now.Date.AddDays(-998), At(14, 1000));

        Assert.Empty(MilestoneSchedule.Upcoming([occasion], Settings(enabled: false), Now, 48));
    }

    [Fact]
    public void Uses_the_configured_time_of_day()
    {
        var occasion = Since(Now.Date.AddDays(-998), At(15, 1000));

        var entry = Assert.Single(MilestoneSchedule.Upcoming([occasion], Settings(hour: 21), Now, 48));

        Assert.Equal(Now.Date.AddDays(2).AddHours(21), entry.When);
    }
}
