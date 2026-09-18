using CommunityToolkit.Maui;
using Markd.Core;
using Markd.Pages;
using Markd.Services;
using Markd.ViewModels;
using Microsoft.Extensions.Logging;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Markd
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseLocalNotification(config => config.AddCategory(new NotificationCategory(NotificationCategoryType.Event)
                {
                    ActionList =
                    [
                        new NotificationAction(NotificationService.OpenActionId) { Title = "Open", Android = { LaunchAppWhenTapped = true } },
                        new NotificationAction(NotificationService.SnoozeActionId) { Title = "Snooze" }
                    ]
                }))
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("Figtree-Regular.ttf", "FigtreeRegular");
                    fonts.AddFont("Figtree-Medium.ttf", "FigtreeMedium");
                    fonts.AddFont("Figtree-SemiBold.ttf", "FigtreeSemiBold");
                    fonts.AddFont("Figtree-Bold.ttf", "FigtreeBold");
                    fonts.AddFont("Figtree-ExtraBold.ttf", "FigtreeExtraBold");
                    fonts.AddFont("IBMPlexMono-Medium.ttf", "PlexMonoMedium");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            InputChrome.Configure();

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "markd.db");
            builder.Services.AddMarkdCore(dbPath);

            builder.Services.AddSingleton<NotificationService>();
            builder.Services.AddSingleton<IShareService, ShareService>();

            builder.Services.AddTransient<OccasionListViewModel>();
            builder.Services.AddTransient<OccasionDetailViewModel>();
            builder.Services.AddTransient<OccasionFormViewModel>();
            builder.Services.AddTransient<MilestoneEditorViewModel>();
            builder.Services.AddTransient<CalendarViewModel>();
            builder.Services.AddTransient<CategoryViewModel>();
            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<AboutViewModel>();

#if WINDOWS
            Desktop.DesktopRegistration.AddDesktopShell(builder.Services);
#else
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<IAppShellService, AppShellService>();
            builder.Services.AddSingleton<IFeedbackService, SnackbarFeedbackService>();
            builder.Services.AddSingleton<IMilestoneMomentPresenter, MilestoneMomentPresenter>();
            builder.Services.AddTransient<IMilestoneEditorService, MilestoneEditorService>();
            builder.Services.AddTransient<MilestoneEditorPage>();
#if ANDROID
            builder.Services.AddSingleton<IActionMenuService, Platforms.Android.ActionMenuService>();
            builder.Services.AddSingleton<IFileExportService, Platforms.Android.DownloadsExportService>();
#else
            builder.Services.AddSingleton<IActionMenuService, ActionSheetMenuService>();
            builder.Services.AddSingleton<IFileExportService, ShareFileExportService>();
#endif
#endif

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
