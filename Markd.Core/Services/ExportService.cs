using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;
using Markd.Core.Domain;

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

        // Container format: ASCII "MARKD1" (6 bytes) + salt(16) + nonce(12) + tag(16) + ciphertext
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MARKD1");

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

            // Derive key using PBKDF2 (Rfc2898) with a strong iteration count. Prefer Argon2 externally when available.
            var salt = RandomNumberGenerator.GetBytes(16);
            const int iterations = 200_000; // reasonable default on modern devices
            using var kdf = new Rfc2898DeriveBytes(passphrase, salt, iterations, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(32);

            // Encrypt with AES-GCM
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plain.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(key))
            {
                aes.Encrypt(nonce, plain, ciphertext, tag, null);
            }

            using var ms = new MemoryStream();
            ms.Write(Magic, 0, Magic.Length);
            ms.Write(salt, 0, salt.Length);
            ms.Write(nonce, 0, nonce.Length);
            ms.Write(tag, 0, tag.Length);
            ms.Write(ciphertext, 0, ciphertext.Length);
            return ms.ToArray();
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