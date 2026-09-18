using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.Services;

namespace Markd.ViewModels;

public class CalendarViewModel : ViewModelBase
{
    private const string NoMilestone = "—";

    private readonly IOccasionService _occasionService;
    private readonly IAppShellService _shellService;
    private DateOnly _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _monthTitle = string.Empty;
    private string _monthSummary = string.Empty;
    private bool _hasLoaded;

    public CalendarViewModel(IOccasionService occasionService, IAppShellService shellService)
    {
        _occasionService = occasionService;
        _shellService = shellService;
        PreviousMonthCommand = new AsyncRelayCommand(() => ShiftAsync(-1));
        NextMonthCommand = new AsyncRelayCommand(() => ShiftAsync(1));
        TodayCommand = new AsyncRelayCommand(GoToTodayAsync);
        OpenOccasionCommand = new AsyncRelayCommand<CalendarComingUpItem?>(OpenOccasionAsync);

        WeakReferenceMessenger.Default.Register<CalendarViewModel, OccasionsChangedMessage>(this, (vm, _) =>
        {
            if (vm._hasLoaded)
                Application.Current?.Dispatcher.Dispatch(async () => await vm.LoadCurrentMonthAsync());
        });
    }

    protected override void OnLanguageChanged()
    {
        if (_hasLoaded)
            Application.Current?.Dispatcher.Dispatch(async () => await LoadCurrentMonthAsync());
    }

    /// <summary>Monday-first weeks; only the weeks the month actually spans.</summary>
    public ObservableCollection<CalendarWeek> Weeks { get; } = new();
    public ObservableCollection<CalendarComingUpItem> ComingUp { get; } = new();

    public string MonthTitle
    {
        get => _monthTitle;
        private set => SetProperty(ref _monthTitle, value);
    }

    public string MonthSummary
    {
        get => _monthSummary;
        private set => SetProperty(ref _monthSummary, value);
    }

    public bool HasComingUp => ComingUp.Count > 0;

    public IAsyncRelayCommand PreviousMonthCommand { get; }
    public IAsyncRelayCommand NextMonthCommand { get; }
    public IAsyncRelayCommand TodayCommand { get; }
    public IAsyncRelayCommand<CalendarComingUpItem?> OpenOccasionCommand { get; }

    public async Task LoadCurrentMonthAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var marks = await _occasionService.GetCalendarMarksAsync(_currentMonth.Year, _currentMonth.Month);
            var occasions = await _occasionService.GetAllAsync();
            BuildMonth(_currentMonth, marks, DateOnly.FromDateTime(DateTime.Today));
            BuildComingUp(occasions);
            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Builds the month grid; public so the layout rules can be tested without a database.</summary>
    public void BuildMonth(DateOnly month, IReadOnlyList<CalendarMark> marks, DateOnly today)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var leading = ((int)first.DayOfWeek + 6) % 7;
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var cellCount = (int)Math.Ceiling((leading + daysInMonth) / 7d) * 7;

        // Build each week completely before adding it: the week template binds to a plain list.
        Weeks.Clear();
        var days = new List<CalendarDayCell>(7);
        for (var i = 0; i < cellCount; i++)
        {
            var dayNumber = i - leading + 1;
            if (dayNumber < 1 || dayNumber > daysInMonth)
            {
                days.Add(CalendarDayCell.Blank(i % 7));
            }
            else
            {
                var date = new DateOnly(month.Year, month.Month, dayNumber);
                var dots = marks
                    .Where(mark => mark.Date == date)
                    .OrderBy(mark => mark.Kind)
                    .Select(mark => CalendarDot.From(mark))
                    .ToList();
                days.Add(new CalendarDayCell(date, i % 7, date == today, dots));
            }

            if (days.Count == 7)
            {
                Weeks.Add(new CalendarWeek(days));
                days = new List<CalendarDayCell>(7);
            }
        }

        var culture = LocalizationManager.Instance.Culture;
        var monthText = first.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", culture);
        MonthTitle = char.ToUpper(monthText[0], culture) + monthText[1..];
        var marked = marks.Count(mark => mark.Date.Year == month.Year && mark.Date.Month == month.Month);
        MonthSummary = marked == 0
            ? Strings.Calendar_NothingMarked
            : Plural.Format("Calendar_MonthSummary", marked);
    }

    /// <summary>Four occasions ranked by days to their next milestone; occasions with nothing ahead go last.</summary>
    public void BuildComingUp(IEnumerable<Occasion> occasions)
    {
        ComingUp.Clear();
        var ranked = occasions
            .Select(o => (Occasion: o, Next: OccasionMath.GetNextMilestone(o, _occasionService.GetDays(o))))
            .OrderBy(x => x.Next?.DaysAway ?? int.MaxValue)
            .ThenBy(x => x.Occasion.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(4);

        foreach (var (occasion, next) in ranked)
        {
            ComingUp.Add(new CalendarComingUpItem(
                occasion.Id,
                occasion.Emoji,
                occasion.ColorHex,
                next?.Label ?? occasion.Title,
                $"{occasion.Title} · {OccasionMath.AnchorPhrase(occasion)}",
                next is null ? NoMilestone : next.ShortText));
        }

        OnPropertyChanged(nameof(HasComingUp));
    }

    private async Task ShiftAsync(int months)
    {
        if (IsBusy)
            return;

        _currentMonth = _currentMonth.AddMonths(months);
        await LoadCurrentMonthAsync();
    }

    private async Task GoToTodayAsync()
    {
        _currentMonth = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        await LoadCurrentMonthAsync();
    }

    private Task OpenOccasionAsync(CalendarComingUpItem? item) =>
        item is null ? Task.CompletedTask : _shellService.GoToAsync($"{nameof(OccasionDetailPage)}?id={item.OccasionId}");
}

public sealed class CalendarWeek(IReadOnlyList<CalendarDayCell> days)
{
    public IReadOnlyList<CalendarDayCell> Days { get; } = days;
}

public sealed record CalendarDayCell(DateOnly? Date, int Column, bool IsToday, IReadOnlyList<CalendarDot> Dots)
{
    public static CalendarDayCell Blank(int column) => new(null, column, false, []);

    public bool IsInMonth => Date is not null;
    public string DayText => Date?.Day.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    public IReadOnlyList<CalendarDot> CompactDots => Dots.Take(3).ToList();
    public IReadOnlyList<CalendarDot> DesktopDots => Dots.Take(4).ToList();
}

/// <summary>A mark under a day number: solid for an anchor date, 45% alpha for a milestone date.</summary>
public sealed record CalendarDot(Color Color, bool IsMilestone)
{
    public static CalendarDot From(CalendarMark mark)
    {
        var color = Controls.HexColor.Parse(mark.ColorHex);
        return mark.Kind == CalendarMarkKind.Milestone
            ? new CalendarDot(color.WithAlpha(0.45f), true)
            : new CalendarDot(color, false);
    }
}

public sealed record CalendarComingUpItem(int OccasionId, string? Emoji, string? ColorHex, string Label, string Sub, string RemainText);
