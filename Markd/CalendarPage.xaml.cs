using Markd.Services;
using Markd.ViewModels;

namespace Markd;

public partial class CalendarPage : ContentPage
{
    private readonly CalendarViewModel _viewModel;

    public CalendarPage()
    {
        InitializeComponent();
        _viewModel = ServiceHelper.GetRequiredService<CalendarViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: true);
        SnackbarFeedbackService.Anchor = NavBar;
        await _viewModel.LoadCurrentMonthAsync();
    }
}
