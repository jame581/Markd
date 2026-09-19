using System.Globalization;
using CommunityToolkit.Maui;
using Markd.Core;
using Markd.Core.Localization;
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

            // Notification actions are registered before the database (and its saved language) is available,
            // so they follow the device language until the app opens and reschedules with the saved setting.
            var startupCulture = LanguageSetting.Resolve(LanguageSetting.System, CultureInfo.CurrentUICulture);
            string Text(string key) => Strings.ResourceManager.GetString(key, startupCulture) ?? key;

            builder
                .UseMauiApp<App>()
                .UseLocalNotification(config => config.AddCategory(new NotificationCategory(NotificationCategoryType.Event)
                {
                    ActionList =
                    [
                        new NotificationAction(NotificationService.OpenActionId) { Title = Text("Notification_Open"), Android = { LaunchAppWhenTapped = true } },
                        new NotificationAction(NotificationService.SnoozeActionId) { Title = Text("Notification_Snooze") }
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

#if ANDROID
            // Essentials' Share stages files under this location before handing them to the share sheet.
            // Default is PreferExternal; force it into the app's internal cache so staged copies live under
            // FileSystem.CacheDirectory, where ShareSheet.ClearStaged() can find and delete them.
            Microsoft.Maui.Storage.FileProvider.TemporaryLocation = Microsoft.Maui.Storage.FileProviderLocation.Internal;
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

            builder.Services.AddSingleton<IFilePickService, FilePickService>();
            builder.Services.AddTransient<BackupCoordinator>();

#if WINDOWS
            Desktop.DesktopRegistration.AddDesktopShell(builder.Services);
#else
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddSingleton<IAppShellService, AppShellService>();
            builder.Services.AddSingleton<IFeedbackService, SnackbarFeedbackService>();
            builder.Services.AddSingleton<IMilestoneMomentPresenter, MilestoneMomentPresenter>();
            builder.Services.AddTransient<IMilestoneEditorService, MilestoneEditorService>();
            builder.Services.AddTransient<MilestoneEditorPage>();
            builder.Services.AddSingleton<IPasswordPromptService, PhonePasswordPromptService>();
#if ANDROID
            builder.Services.AddSingleton<IActionMenuService, Platforms.Android.ActionMenuService>();
            builder.Services.AddSingleton<IFileExportService, Platforms.Android.DocumentExportService>();
#else
            builder.Services.AddSingleton<IActionMenuService, ActionSheetMenuService>();
            builder.Services.AddSingleton<IFileExportService, FileSaverExportService>();
#endif
#endif

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
