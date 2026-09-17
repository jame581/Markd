using Markd.Core;
using Markd.Services;
using Markd.ViewModels;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using CommunityToolkit.Maui;

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
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("Figtree-Regular.ttf",  "FigtreeRegular");
                    fonts.AddFont("Figtree-Medium.ttf",   "FigtreeMedium");
                    fonts.AddFont("Figtree-SemiBold.ttf", "FigtreeSemiBold");
                    fonts.AddFont("Figtree-Bold.ttf",     "FigtreeBold");
                    fonts.AddFont("Figtree-ExtraBold.ttf","FigtreeExtraBold");
                    fonts.AddFont("IBMPlexMono-Medium.ttf", "PlexMonoMedium");
                    // keep OpenSans registered until every page is migrated
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "markd.db");
            builder.Services.AddMarkdCore(dbPath);

            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<IAppShellService, AppShellService>();
            builder.Services.AddSingleton<NotificationService>();
            builder.Services.AddSingleton<IShareService, ShareService>();
            builder.Services.AddTransient<OccasionListViewModel>();
            builder.Services.AddTransient<OccasionFormViewModel>();
            builder.Services.AddTransient<OccasionDetailViewModel>();
            builder.Services.AddTransient<CategoryViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();

            // Export / Import UI + VM for testing
            builder.Services.AddTransient<ViewModels.ExportImportViewModel>();
            builder.Services.AddTransient<Pages.ExportImportPage>();

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
