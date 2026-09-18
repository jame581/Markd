using Markd.ViewModels;

namespace Markd.Desktop.Views;

/// <summary>Month grid beside "Coming up" and "This month"; the side column drops under the grid when the window is narrow.</summary>
public partial class CalendarView : ContentView, IDesktopSection
{
    private readonly CalendarViewModel _viewModel;
    private bool _stacked;

    public CalendarView()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<CalendarViewModel>();
        BindingContext = _viewModel;
        SizeChanged += (_, _) => UpdateLayout();
    }

    public event EventHandler? HeaderChanged;

    public string Title => "Calendar";
    public string Subtitle => "Anchor dates and milestones across the month";
    public bool ShowsNewOccasion => false;
    public bool HasHeaderRule => true;
    public string? BackLabel => null;

    public Task ShowAsync() => _viewModel.LoadCurrentMonthAsync();

    public void Hide()
    {
    }

    public bool GoBack() => false;

    private void UpdateLayout()
    {
        var stacked = Width > 0 && Width < 880;
        if (stacked == _stacked)
            return;

        _stacked = stacked;
        Columns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(320);
        Grid.SetColumn(SideColumn, stacked ? 0 : 1);
        Grid.SetRow(SideColumn, stacked ? 1 : 0);
        Columns.ColumnSpacing = stacked ? 0 : 20;
        HeaderChanged?.Invoke(this, EventArgs.Empty);
    }
}
