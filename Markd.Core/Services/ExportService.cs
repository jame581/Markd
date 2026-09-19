using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;

namespace Markd.Core.Services
{
    public class ExportService : IExportService
    {
        private readonly MarkdDbContext _db;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ExportService(MarkdDbContext db)
        {
            _db = db;
        }

        public async Task<byte[]> CreateExportJsonAsync()
        {
            var model = await BuildExportModelAsync();
            var json = JsonSerializer.Serialize(model, _jsonOptions);
            return Encoding.UTF8.GetBytes(json);
        }

        public async Task<byte[]> CreateExportPackageAsync(string? passphrase = null)
        {
            var plain = await CreateExportJsonAsync();
            if (string.IsNullOrEmpty(passphrase))
                return plain;

            try
            {
                // Key derivation takes about a second on a slow phone; keep it off the UI thread.
                return await Task.Run(() => MarkdPackage.Encrypt(plain, passphrase));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }

        private async Task<ExportModel> BuildExportModelAsync()
        {
            var model = new ExportModel
            {
                SchemaVersion = "1",
                AppVersion = "", // populate from app if available
                ExportedAt = DateTime.UtcNow,
                DeviceId = Environment.MachineName
            };

            var categories = await _db.Categories.AsNoTracking().ToListAsync();
            foreach (var c in categories)
            {
                model.Categories.Add(new CategoryDto
                {
                    SourceId = c.Id,
                    Name = c.Name,
                    Emoji = c.Emoji,
                    ColorHex = c.ColorHex
                });
            }

            var occasions = await _db.Occasions.AsNoTracking().ToListAsync();
            foreach (var o in occasions)
            {
                model.Occasions.Add(new OccasionDto
                {
                    SourceId = o.Id,
                    Title = o.Title,
                    Emoji = o.Emoji,
                    ColorHex = o.ColorHex,
                    IsPinned = o.IsPinned,
                    AnchorDate = o.AnchorDate,
                    Direction = o.Direction.ToString(),
                    Notes = o.Notes,
                    CreatedAt = o.CreatedAt,
                    CategorySourceId = o.CategoryId
                });
            }

            var milestones = await _db.Milestones.AsNoTracking().ToListAsync();
            foreach (var m in milestones)
            {
                model.Milestones.Add(new MilestoneDto
                {
                    SourceId = m.Id,
                    OccasionSourceId = m.OccasionId,
                    ThresholdDays = m.ThresholdDays,
                    Label = m.Label,
                    Notified = m.Notified
                });
            }

            var settings = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
            if (settings != null)
            {
                model.Settings = new AppSettingsDto
                {
                    Theme = settings.Theme,
                    Language = settings.Language,
                    NotificationsEnabled = settings.NotificationsEnabled
                };
            }

            return model;
        }
    }
}