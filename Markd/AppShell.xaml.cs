namespace Markd
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            Routing.RegisterRoute(nameof(OccasionFormPage), typeof(OccasionFormPage));
            Routing.RegisterRoute(nameof(OccasionDetailPage), typeof(OccasionDetailPage));
            Routing.RegisterRoute(nameof(CategoryPage), typeof(CategoryPage));

            Items.Add(new ShellContent
            {
                Title = "Dashboard",
                Icon = "🏠",
                Route = nameof(MainPage),
                ContentTemplate = new DataTemplate(typeof(MainPage))
            });

            Items.Add(new ShellContent
            {
                Title = "Settings",
                Icon = "⚙️",
                Route = nameof(SettingsPage),
                ContentTemplate = new DataTemplate(typeof(SettingsPage))
            });
        }
    }
}
