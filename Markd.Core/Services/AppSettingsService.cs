using Markd.Core.Data;
using Markd.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Services
{
    public class AppSettingsService : IAppSettingsService
    {
        private readonly MarkdDbContext db;

        public AppSettingsService(MarkdDbContext db)
        {
            this.db = db;
        }

        public async Task<AppSettings> GetAsync()
        {
            var settings = await db.AppSettings.FirstOrDefaultAsync();

            if (settings is null)
            {
                settings = new AppSettings { Id = 1 };
                db.AppSettings.Add(settings);
                await db.SaveChangesAsync();
            }

            return settings;
        }

        public async Task SaveAsync(AppSettings settings)
        {
            var existing = await db.AppSettings.FindAsync(settings.Id);

            if (existing is null)
            {
                db.AppSettings.Add(settings);
            }
            else
            {
                existing.Theme = settings.Theme;
                existing.Language = settings.Language;
                existing.NotificationsEnabled = settings.NotificationsEnabled;
                existing.NotificationTimeOfDay = settings.NotificationTimeOfDay;
            }

            await db.SaveChangesAsync();
        }
    }
}
