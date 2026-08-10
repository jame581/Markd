namespace Markd
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            Routing.RegisterRoute(nameof(OccasionFormPage), typeof(OccasionFormPage));
            Routing.RegisterRoute(nameof(OccasionDetailPage), typeof(OccasionDetailPage));

            Items.Add(new ShellContent
            {
                Title = "Dashboard",
                Route = nameof(MainPage),
                ContentTemplate = new DataTemplate(typeof(MainPage))
            });
        }
    }
}
