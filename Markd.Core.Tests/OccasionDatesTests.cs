using Markd.Core.Domain;
using Xunit;

namespace Markd.Core.Tests;

public class OccasionDatesTests
{
    private static readonly TimeZoneInfo Prague =
        TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");

    [Fact]
    public void ToLocalDate_TreatsStoredValueAsUtc()
    {
        // 28 Apr local midnight in UTC+2 is stored as 27 Apr 22:00 UTC and read back as Unspecified.
        var stored = new DateTime(2024, 4, 27, 22, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateTime(2024, 4, 28), OccasionDates.ToLocalDate(stored, Prague));
    }

    [Fact]
    public void GetDays_CountsBetweenLocalDates()
    {
        var occasion = new Occasion
        {
            AnchorDate = new DateTime(2024, 4, 27, 22, 0, 0, DateTimeKind.Utc),
            Direction = OccasionDirection.Since
        };

        Assert.Equal(10, OccasionDates.GetDays(occasion, new DateTime(2024, 5, 8, 0, 30, 0), Prague));
    }

    [Fact]
    public void GetDays_UntilIsNegativeOncePassed()
    {
        var occasion = new Occasion
        {
            AnchorDate = new DateTime(2026, 12, 18, 23, 0, 0, DateTimeKind.Utc),
            Direction = OccasionDirection.Until
        };

        Assert.Equal(92, OccasionDates.GetDays(occasion, new DateTime(2026, 9, 18), Prague));
        Assert.Equal(-2, OccasionDates.GetDays(occasion, new DateTime(2026, 12, 21), Prague));
    }

    [Theory]
    [InlineData(OccasionDirection.Since, 1000, 2025, 7, 10)]
    [InlineData(OccasionDirection.Until, 30, 2022, 9, 14)]
    public void GetMilestoneDate_AddsForSinceAndSubtractsForUntil(OccasionDirection direction, int threshold, int year, int month, int day)
    {
        var occasion = new Occasion
        {
            AnchorDate = new DateTime(2022, 10, 13, 22, 0, 0, DateTimeKind.Utc),
            Direction = direction
        };

        var date = OccasionDates.GetMilestoneDate(occasion, new Milestone { ThresholdDays = threshold }, Prague);

        Assert.Equal(new DateTime(year, month, day), date);
    }

    [Theory]
    [InlineData(OccasionDirection.Since, 1000, 1435, true)]
    [InlineData(OccasionDirection.Since, 1461, 1435, false)]
    [InlineData(OccasionDirection.Until, 30, 92, false)]
    [InlineData(OccasionDirection.Until, 30, 12, true)]
    public void IsMilestoneReached_UsesDirection(OccasionDirection direction, int threshold, int days, bool expected)
    {
        var occasion = new Occasion { Direction = direction };

        Assert.Equal(expected, OccasionDates.IsMilestoneReached(occasion, new Milestone { ThresholdDays = threshold }, days));
    }
}
