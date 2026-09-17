namespace Markd;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(OccasionFormPage), typeof(OccasionFormPage));
        Routing.RegisterRoute(nameof(OccasionDetailPage), typeof(OccasionDetailPage));
        Routing.RegisterRoute(nameof(CategoryPage), typeof(CategoryPage));
        Routing.RegisterRoute(nameof(CalendarPage), typeof(CalendarPage));
        Routing.RegisterRoute(nameof(AboutPage), typeof(AboutPage));
        Routing.RegisterRoute(nameof(Pages.ExportImportPage), typeof(Pages.ExportImportPage));
    }
}
