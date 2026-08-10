using Markd.Core;
using Markd.ViewModels;
using Microsoft.Extensions.Logging;

namespace Markd
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "markd.db");
            builder.Services.AddMarkdCore(dbPath);

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<OccasionListViewModel>();
            builder.Services.AddTransient<OccasionFormViewModel>();
            builder.Services.AddTransient<OccasionDetailViewModel>();
            builder.Services.AddTransient<CategoryViewModel>();

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
