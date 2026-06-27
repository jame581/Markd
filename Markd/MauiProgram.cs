using Microsoft.Extensions.Logging;
using Markd.Core;

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

            var app = builder.Build();
            app.Services.InitializeMarkdDatabase();

            return app;
        }
    }
}
