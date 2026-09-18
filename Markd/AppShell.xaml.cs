namespace Markd;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(OccasionFormPage), typeof(OccasionFormPage));
        Routing.RegisterRoute(nameof(OccasionDetailPage), typeof(OccasionDetailPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
    }
}
