using Markd.Services;
using Markd.ViewModels;

namespace Markd;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        BindingContext = ServiceHelper.GetRequiredService<AboutViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SystemBars.Apply(withNavigationBar: false);
        SnackbarFeedbackService.Anchor = null;
    }
}
