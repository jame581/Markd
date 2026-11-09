using System.Globalization;
using CommunityToolkit.Maui;
using Markd.Core;
using Markd.Core.Localization;
using Markd.Core.Sync;
using Markd.Pages;
using Markd.Services;
using Markd.Services.Sync;
using Markd.Services.Sync.Cloud;
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

            var dataDirectory = FileSystem.AppDataDirectory;
#if DEBUG
            // Lets two debug instances on one PC keep separate data (database and Windows sync secret) and sync with
            // each other through the developer folder provider.
            var dataOverride = Environment.GetEnvironmentVariable("MARKD_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(dataOverride))
            {
                dataDirectory = Path.GetFullPath(dataOverride);
                Directory.CreateDirectory(dataDirectory);
            }
#endif

            var dbPath = Path.Combine(dataDirectory, "markd.db");
            builder.Services.AddMarkdCore(dbPath);

#if WINDOWS
            builder.Services.AddSingleton<ISyncSecretStore>(sp => new Platforms.Windows.DpapiSyncSecretStore(
                dataDirectory, sp.GetRequiredService<ILogger<Platforms.Windows.DpapiSyncSecretStore>>()));
#else
            builder.Services.AddSingleton<ISyncSecretStore, SecureStorageSyncSecretStore>();
#endif
            builder.Services.AddSingleton<ICloudProviderCatalog>(sp => new CloudProviderCatalog(CloudProviders(sp, dataDirectory)));
            builder.Services.AddSingleton<IUiDispatcher, MainThreadDispatcher>();
            builder.Services.AddSingleton<ISyncDataRefresher, MauiSyncDataRefresher>();
            builder.Services.AddSingleton<INetworkStatus, MauiNetworkStatus>();
            // Sync runs on its own scope and DbContext; never inject ISyncService into app classes.
            builder.Services.AddSingleton<ISyncSession, SyncScope>();
            // One coordinator for the app: settings, backups and the scheduler all talk to the same instance.
            builder.Services.AddSingleton<SyncCoordinator>();
            builder.Services.AddSingleton<ISyncController>(sp => sp.GetRequiredService<SyncCoordinator>());
            builder.Services.AddSingleton<ISyncLink>(sp => sp.GetRequiredService<SyncCoordinator>());
            // Changes a sync applied, and the notification service's own reschedule messages, are not local edits.
            builder.Services.AddSingleton(sp => new SyncScheduler(
                sp.GetRequiredService<ISyncController>(),
                sp.GetRequiredService<INetworkStatus>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IUiDispatcher>(),
                source => source is MauiSyncDataRefresher or NotificationService));
            builder.Services.AddTransient<SyncSettingsViewModel>();

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
            builder.Services.AddSingleton<IConflictResolutionService, PhoneConflictResolutionService>();
            builder.Services.AddSingleton<ISyncPassphrasePromptService, PhoneSyncPassphrasePromptService>();
            builder.Services.AddSingleton<ISignInWaitService, PhoneSignInWaitService>();
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

        /// <summary>
        /// Every provider this app knows, with whether this build and device can use it. Google needs its desktop
        /// client id on Windows and Play services on Android; OneDrive needs the Microsoft client id on Android and
        /// Windows. Other platforms only keep their names.
        /// </summary>
        private static List<CloudProviderRegistration> CloudProviders(IServiceProvider sp, string dataDirectory)
        {
#if WINDOWS
            ICloudAuthService? googleAuth = CloudConfig.HasGoogleDesktop
                ? new Platforms.Windows.GoogleWindowsAuthService(
                    dataDirectory, sp.GetRequiredService<ILogger<Platforms.Windows.GoogleWindowsAuthService>>())
                : null;
#elif ANDROID
            ICloudAuthService? googleAuth = Platforms.Android.GoogleAndroidAuthService.IsAvailable()
                ? new Platforms.Android.GoogleAndroidAuthService(
                    sp.GetRequiredService<INetworkStatus>(), sp.GetRequiredService<ILogger<Platforms.Android.GoogleAndroidAuthService>>())
                : null;
#else
            ICloudAuthService? googleAuth = null;
#endif

#if ANDROID || WINDOWS
            ICloudAuthService? microsoftAuth = CloudConfig.HasMicrosoft
                ? new MsalAuthService(
                    dataDirectory, sp.GetRequiredService<INetworkStatus>(), sp.GetRequiredService<ILogger<MsalAuthService>>())
                : null;
#else
            ICloudAuthService? microsoftAuth = null;
#endif

            // Brand name, not localized.
            var providers = new List<CloudProviderRegistration>
            {
                new(GoogleDriveProvider.ProviderId, "Google Drive", googleAuth is not null,
                    () => new GoogleDriveProvider(
                        new HttpClient { Timeout = TimeSpan.FromSeconds(60) },
                        googleAuth!,
                        sp.GetRequiredService<ILogger<GoogleDriveProvider>>()),
                    googleAuth),
                // OneDriveProvider follows Graph's download redirect itself (without the bearer token), so its
                // handler must not. SocketsHttpHandler on every platform, so this does not depend on how Android's
                // default AndroidMessageHandler treats AllowAutoRedirect.
                new(OneDriveProvider.ProviderId, "OneDrive", microsoftAuth is not null,
                    () => new OneDriveProvider(
                        new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) },
                        microsoftAuth!,
                        sp.GetRequiredService<ILogger<OneDriveProvider>>()),
                    microsoftAuth),
            };
#if DEBUG
            providers.Add(DebugLocalFolder.Registration);
#endif
            return providers;
        }
    }
}
