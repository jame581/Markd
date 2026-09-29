using Markd.Core.Data;
using Markd.Core.Services;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Markd.Core
{
    public static class MarkdCoreServiceExtensions
    {
        /// <summary>
        /// Registers MarkdDbContext with SQLite.
        /// Call from MauiProgram.cs, passing the platform-specific db path.
        /// </summary>
        public static IServiceCollection AddMarkdCore(this IServiceCollection services, string databasePath)
        {
            services.AddDbContext<MarkdDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

            services.AddScoped<IOccasionService, OccasionService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IAppSettingsService, AppSettingsService>();
            services.AddScoped<IExportService, ExportService>();
            services.AddScoped<IImportService, ImportService>();
            services.AddScoped<ISnapshotService, SnapshotService>();
            services.AddScoped<ISyncStateStore, SyncStateStore>();
            services.AddSingleton(new SyncOptions());
            services.TryAddSingleton(TimeProvider.System);
            services.AddScoped<SyncService>();
            services.AddScoped<ISyncService>(sp => sp.GetRequiredService<SyncService>());

            return services;
        }

        /// <summary>
        /// Applies any pending migrations and seeds default data.
        /// Call once on app startup after the service provider is built.
        /// </summary>
        public static void InitializeMarkdDatabase(this IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarkdDbContext>();
            db.Database.Migrate();
            DatabaseBackups.DeleteStale(db.Database.GetDbConnection().DataSource, TimeSpan.FromHours(24));
        }
    }
}
