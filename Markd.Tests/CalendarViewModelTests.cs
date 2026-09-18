using Markd.Core.Services;
using Markd.ViewModels;
using Xunit;

namespace Markd.Tests;

public class CalendarViewModelTests
{
    // BuildMonth only lays out the grid, so the view model needs no services here.
    private static CalendarViewModel CreateViewModel() => new(null!, null!);

    [Theory]
    [InlineData(2026, 9, 5)]  // starts on a Tuesday, 30 days
    [InlineData(2026, 3, 6)]  // starts on a Sunday, 31 days
    [InlineData(2027, 2, 4)]  // starts on a Monday, 28 days
    public void BuildMonth_DrawsOnlyTheWeeksTheMonthSpans(int year, int month, int expectedWeeks)
    {
        var viewModel = CreateViewModel();

        viewModel.BuildMonth(new DateOnly(year, month, 1), [], new DateOnly(2026, 9, 18));

        Assert.Equal(expectedWeeks, viewModel.Weeks.Count);
        Assert.All(viewModel.Weeks, week => Assert.Equal(7, week.Days.Count));
    }

    [Fact]
    public void BuildMonth_WeeksStartOnMonday()
    {
        var viewModel = CreateViewModel();

        viewModel.BuildMonth(new DateOnly(2026, 9, 1), [], new DateOnly(2026, 9, 18));

        var firstWeek = viewModel.Weeks[0].Days;
        Assert.False(firstWeek[0].IsInMonth);
        Assert.Equal(new DateOnly(2026, 9, 1), firstWeek[1].Date);
        Assert.Equal(1, firstWeek[1].Column);
        Assert.Equal("September 2026", viewModel.MonthTitle);
    }

    [Fact]
    public void BuildMonth_MarksTodayOnly()
    {
        var viewModel = CreateViewModel();

        viewModel.BuildMonth(new DateOnly(2026, 9, 1), [], new DateOnly(2026, 9, 18));

        var today = Assert.Single(viewModel.Weeks.SelectMany(w => w.Days), d => d.IsToday);
        Assert.Equal(new DateOnly(2026, 9, 18), today.Date);
    }

    [Fact]
    public void BuildMonth_AnchorDotsAreSolidAndMilestoneDotsFaded()
    {
        var viewModel = CreateViewModel();
        var day = new DateOnly(2026, 9, 19);
        CalendarMark[] marks =
        [
            new(day, 2, "Sober days", "🌱", "#0B8043", CalendarMarkKind.Milestone, "Five sixty-five", 565, false),
            new(day, 5, "Gym streak", "🏃", "#E53935", CalendarMarkKind.Anchor, null, null, false)
        ];

        viewModel.BuildMonth(new DateOnly(2026, 9, 1), marks, new DateOnly(2026, 9, 18));

        var cell = viewModel.Weeks.SelectMany(w => w.Days).Single(d => d.Date == day);
        Assert.Collection(
            cell.Dots,
            anchor => { Assert.False(anchor.IsMilestone); Assert.Equal(1f, anchor.Color.Alpha, 3); },
            milestone => { Assert.True(milestone.IsMilestone); Assert.Equal(0.45f, milestone.Color.Alpha, 3); });
        Assert.StartsWith("2 dates marked", viewModel.MonthSummary);
    }

    [Fact]
    public void BuildMonth_CapsDotsPerPlatform()
    {
        var viewModel = CreateViewModel();
        var day = new DateOnly(2026, 9, 10);
        var marks = Enumerable.Range(1, 5)
            .Select(i => new CalendarMark(day, i, $"Occasion {i}", null, "#3F51B5", CalendarMarkKind.Anchor, null, null, false))
            .ToList();

        viewModel.BuildMonth(new DateOnly(2026, 9, 1), marks, new DateOnly(2026, 9, 18));

        var cell = viewModel.Weeks.SelectMany(w => w.Days).Single(d => d.Date == day);
        Assert.Equal(3, cell.CompactDots.Count);
        Assert.Equal(4, cell.DesktopDots.Count);
    }

    [Fact]
    public void BuildMonth_EmptyMonthSaysSo()
    {
        var viewModel = CreateViewModel();

        viewModel.BuildMonth(new DateOnly(2026, 11, 1), [], new DateOnly(2026, 9, 18));

        Assert.Equal("No anchors or milestones fall in this month.", viewModel.MonthSummary);
    }
}
