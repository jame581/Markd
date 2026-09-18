using Markd.Core.Domain;
using Markd.ViewModels;
using Xunit;

namespace Markd.Tests;

[Collection(nameof(GlobalCultureCollection))]
public class OccasionMathTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Test+02", TimeSpan.FromHours(2), "Test+02", "Test+02");

    // 14 Oct 2022 local midnight in UTC+2.
    private static readonly DateTime AnniversaryAnchor = new(2022, 10, 13, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FormatBreakdown_Since_IsCalendarAccurate()
    {
        var occasion = new Occasion { AnchorDate = AnniversaryAnchor, Direction = OccasionDirection.Since };

        var text = OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 8, 41, 9), Zone);

        Assert.Equal("3y 11mo 4d 08h 41min 09s", text);
    }

    [Fact]
    public void FormatBreakdown_DropsLeadingZeroUnits()
    {
        var occasion = new Occasion { AnchorDate = new DateTime(2026, 9, 3, 22, 0, 0, DateTimeKind.Utc), Direction = OccasionDirection.Since };

        Assert.Equal("14d 06h 05min 00s", OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 6, 5, 0), Zone));
    }

    [Fact]
    public void FormatBreakdown_Until_BorrowsADayWhenTimeOfDayWraps()
    {
        // From 18 Sep 08:26 to 19 Dec 00:00 is 3 months and 15h 34min, not "3mo 1d".
        var occasion = new Occasion { AnchorDate = new DateTime(2026, 12, 18, 23, 0, 0, DateTimeKind.Utc), Direction = OccasionDirection.Until };

        Assert.Equal("3mo 0d 15h 34min 00s", OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 8, 26, 0), Zone));
    }

    [Fact]
    public void FormatBreakdown_UntilInThePast_IsNegative()
    {
        var occasion = new Occasion { AnchorDate = new DateTime(2026, 9, 9, 22, 0, 0, DateTimeKind.Utc), Direction = OccasionDirection.Until };

        Assert.StartsWith("-8d", OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 0, 0, 0), Zone));
    }

    [Fact]
    public void FormatBreakdown_UntilOnTheDay_ReadsZero()
    {
        var occasion = new Occasion { AnchorDate = new DateTime(2026, 9, 17, 22, 0, 0, DateTimeKind.Utc), Direction = OccasionDirection.Until };

        Assert.Equal("0d 00h 00min 00s", OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 14, 12, 3), Zone));
    }

    [Fact]
    public void GetNextMilestone_Since_MeasuresFromPreviousMilestone()
    {
        var occasion = new Occasion
        {
            AnchorDate = AnniversaryAnchor,
            Direction = OccasionDirection.Since,
            Milestones =
            [
                new Milestone { ThresholdDays = 1000, Label = "Four digits" },
                new Milestone { ThresholdDays = 1461, Label = "Four years" },
                new Milestone { ThresholdDays = 1500, Label = "Fifteen hundred" }
            ]
        };

        var next = OccasionMath.GetNextMilestone(occasion, 1435, Zone);

        Assert.NotNull(next);
        Assert.Equal("Four years", next.Label);
        Assert.Equal(26, next.DaysAway);
        Assert.Equal((1435 - 1000) / 461d, next.Progress, 3);
        Assert.Equal("26 days to go", next.DaysToGoText);
    }

    [Fact]
    public void GetNextMilestone_Since_ReturnsNullWhenAllReached()
    {
        var occasion = new Occasion
        {
            Direction = OccasionDirection.Since,
            Milestones = [new Milestone { ThresholdDays = 100, Label = "Hundred" }]
        };

        Assert.Null(OccasionMath.GetNextMilestone(occasion, 150, Zone));
    }

    [Fact]
    public void GetNextMilestone_Until_CountsDownToTheLargestRemainingThreshold()
    {
        var occasion = new Occasion
        {
            AnchorDate = new DateTime(2026, 12, 18, 23, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc),
            Direction = OccasionDirection.Until,
            Milestones =
            [
                new Milestone { ThresholdDays = 30, Label = "One month to go" },
                new Milestone { ThresholdDays = 7, Label = "One week to go" }
            ]
        };

        var next = OccasionMath.GetNextMilestone(occasion, 92, Zone);

        Assert.NotNull(next);
        Assert.Equal("One month to go", next.Label);
        Assert.Equal(62, next.DaysAway);
        Assert.Equal(109 / 171d, next.Progress, 3);
    }

    [Theory]
    [InlineData(OccasionDirection.Since, 12, "days")]
    [InlineData(OccasionDirection.Since, -5, "to go")]
    [InlineData(OccasionDirection.Until, 92, "to go")]
    [InlineData(OccasionDirection.Until, -3, "days ago")]
    public void UnitLabel_DependsOnDirection(OccasionDirection direction, int days, string expected)
    {
        Assert.Equal(expected, OccasionMath.UnitLabel(new Occasion { Direction = direction }, days));
    }

    [Fact]
    public void AnchorPhrase_UsesLocalDate()
    {
        var occasion = new Occasion { AnchorDate = AnniversaryAnchor, Direction = OccasionDirection.Since };

        Assert.Equal("since 14 Oct 2022", OccasionMath.AnchorPhrase(occasion, Zone));
    }

    [Fact]
    public void FormatBreakdown_Czech_UsesCzechUnits()
    {
        using var _ = new CultureScope("cs-CZ");
        var occasion = new Occasion { AnchorDate = AnniversaryAnchor, Direction = OccasionDirection.Since };

        Assert.Equal("3r 11m 4d 08h 41min 09s", OccasionMath.FormatBreakdown(occasion, new DateTime(2026, 9, 18, 8, 41, 9), Zone));
    }

    [Fact]
    public void Dates_FollowCulture()
    {
        var date = new DateTime(2022, 10, 14);
        Assert.Equal("14 Oct 2022", OccasionMath.FormatShortDate(date));

        using var _ = new CultureScope("cs-CZ");
        Assert.Equal("14. 10. 2022", OccasionMath.FormatShortDate(date));
        Assert.Equal("pátek 14. října 2022", OccasionMath.FormatLongDate(date));
    }

    [Theory]
    [InlineData(0, "Dnes")]
    [InlineData(1, "za 1 den")]
    [InlineData(3, "za 3 dny")]
    [InlineData(12, "za 12 dní")]
    [InlineData(-1, "před 1 dnem")]
    [InlineData(-6, "před 6 dny")]
    public void FormatDaysAway_Czech(int days, string expected)
    {
        using var _ = new CultureScope("cs-CZ");
        Assert.Equal(expected, OccasionMath.FormatDaysAway(days));
    }

    [Theory]
    [InlineData(1, "1 day to go")]
    [InlineData(5, "5 days to go")]
    public void DaysToGo_English(int days, string expected) =>
        Assert.Equal(expected, new NextMilestoneInfo(new Milestone { Label = "x" }, days, 0.5).DaysToGoText);

    [Theory]
    [InlineData(1, "zbývá 1 den")]
    [InlineData(2, "zbývají 2 dny")]
    [InlineData(5, "zbývá 5 dní")]
    public void DaysToGo_Czech(int days, string expected)
    {
        using var _ = new CultureScope("cs-CZ");
        Assert.Equal(expected, new NextMilestoneInfo(new Milestone { Label = "x" }, days, 0.5).DaysToGoText);
    }

    [Fact]
    public void AnchorPhrase_Czech()
    {
        using var _ = new CultureScope("cs-CZ");
        var occasion = new Occasion { AnchorDate = AnniversaryAnchor, Direction = OccasionDirection.Since };
        Assert.Equal("od 14. 10. 2022", OccasionMath.AnchorPhrase(occasion, Zone));
    }

    [Theory]
    [InlineData(1, "zbývá")]
    [InlineData(3, "zbývají")]
    [InlineData(12, "zbývá")]
    public void UnitLabel_UntilToGo_Czech_Pluralizes(int days, string expected)
    {
        using var _ = new CultureScope("cs-CZ");
        var occasion = new Occasion { Direction = OccasionDirection.Until };
        Assert.Equal(expected, OccasionMath.UnitLabel(occasion, days));
    }

    [Theory]
    [InlineData(1, "to go")]
    [InlineData(3, "to go")]
    [InlineData(12, "to go")]
    public void UnitLabel_UntilToGo_English_StaysToGo(int days, string expected)
    {
        var occasion = new Occasion { Direction = OccasionDirection.Until };
        Assert.Equal(expected, OccasionMath.UnitLabel(occasion, days));
    }
}
