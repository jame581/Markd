using Markd.Core;
using Markd.Services;
using Markd.ViewModels;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;

namespace Markd
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLocalNotification()
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
            builder.Services.AddSingleton<NotificationService>();
            builder.Services.AddTransient<OccasionListViewModel>();
            builder.Services.AddTransient<OccasionFormViewModel>();
            builder.Services.AddTransient<OccasionDetailViewModel>();
            builder.Services.AddTransient<CategoryViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
