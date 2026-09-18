using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Localization;

namespace Markd.Core.Services
{
    public class ImportService : IImportService
    {
        private readonly MarkdDbContext _db;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MARKD1");

        public ImportService(MarkdDbContext db)
        {
            _db = db;
        }

        public async Task<ExportModel> ParseImportPackageAsync(byte[] package, string? passphrase = null)
        {
            if (package == null || package.Length == 0)
                throw new InvalidOperationException(Strings.Import_EmptyFile);

            if (package.Length >= Magic.Length && package.Take(Magic.Length).SequenceEqual(Magic))
            {
                var idx = Magic.Length;
                var salt = package.Skip(idx).Take(16).ToArray(); idx += 16;
                var nonce = package.Skip(idx).Take(12).ToArray(); idx += 12;
                var tag = package.Skip(idx).Take(16).ToArray(); idx += 16;
                var ciphertext = package.Skip(idx).ToArray();

                if (string.IsNullOrEmpty(passphrase))
                    throw new InvalidOperationException(Strings.Import_PassphraseRequired);

                const int iterations = 200_000;
                const int iterationsLocal = iterations;
                using var kdf = new Rfc2898DeriveBytes(passphrase, salt, iterationsLocal, HashAlgorithmName.SHA256);
                var key = kdf.GetBytes(32);

                var plain = new byte[ciphertext.Length];
                try
                {
                    using var aes = new AesGcm(key);
                    aes.Decrypt(nonce, ciphertext, tag, plain, null);
                }
                catch (CryptographicException ex)
                {
                    throw new InvalidOperationException(Strings.Import_DecryptFailed, ex);
                }

                return ParseAndValidateImportJson(Encoding.UTF8.GetString(plain));
            }

            return ParseAndValidateImportJson(Encoding.UTF8.GetString(package));
        }

        public async Task ApplyImportAsync(ExportModel model)
        {
            // Resolve DB file path from connection string (SQLite)
            var conn = (SqliteConnection)_db.Database.GetDbConnection();
            var cs = conn.ConnectionString;
            var dataSource = GetSqliteDataSource(cs);
            string? backupPath = null;
            if (!string.IsNullOrEmpty(dataSource) && File.Exists(dataSource))
            {
                backupPath = dataSource + $".backup-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
                File.Copy(dataSource, backupPath, overwrite: true);
            }

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var existingMilestones = await _db.Milestones.ToListAsync();
                var existingOccasions = await _db.Occasions.ToListAsync();
                var existingCategories = await _db.Categories.ToListAsync();

                if (existingMilestones.Count > 0)
                    _db.Milestones.RemoveRange(existingMilestones);

                if (existingOccasions.Count > 0)
                    _db.Occasions.RemoveRange(existingOccasions);

                if (existingCategories.Count > 0)
                    _db.Categories.RemoveRange(existingCategories);

                await _db.SaveChangesAsync();

                var categoryMap = new System.Collections.Generic.Dictionary<int, int>();
                foreach (var c in model.Categories)
                {
                    var category = new Category
                    {
                        Name = c.Name,
                        Emoji = c.Emoji,
                        ColorHex = c.ColorHex
                    };

                    _db.Categories.Add(category);
                    await _db.SaveChangesAsync();
                    categoryMap[c.SourceId] = category.Id;
                }

                var occasionMap = new System.Collections.Generic.Dictionary<int, int>();
                foreach (var o in model.Occasions)
                {
                    var occasion = new Occasion
                    {
                        Title = o.Title,
                        Emoji = o.Emoji,
                        ColorHex = o.ColorHex,
                        IsPinned = o.IsPinned,
                        AnchorDate = o.AnchorDate,
                        Direction = Enum.TryParse<OccasionDirection>(o.Direction, true, out var direction) ? direction : OccasionDirection.Since,
                        Notes = o.Notes,
                        CreatedAt = o.CreatedAt,
                        CategoryId = o.CategorySourceId.HasValue && categoryMap.TryGetValue(o.CategorySourceId.Value, out var categoryId)
                            ? categoryId
                            : null
                    };

                    _db.Occasions.Add(occasion);
                    await _db.SaveChangesAsync();
                    occasionMap[o.SourceId] = occasion.Id;
                }

                foreach (var m in model.Milestones)
                {
                    if (!occasionMap.TryGetValue(m.OccasionSourceId, out var occasionId))
                        continue;

                    _db.Milestones.Add(new Milestone
                    {
                        OccasionId = occasionId,
                        ThresholdDays = m.ThresholdDays,
                        Label = m.Label,
                        Notified = m.Notified
                    });
                }

                var settingsData = model.Settings ?? new AppSettingsDto();
                var settings = await _db.AppSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new Core.Domain.AppSettings();
                    _db.AppSettings.Add(settings);
                }

                settings.Theme = settingsData.Theme;
                settings.Language = LanguageSetting.IsValid(settingsData.Language) ? settingsData.Language : LanguageSetting.System;
                settings.NotificationsEnabled = settingsData.NotificationsEnabled;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                // The rollback restores the data. Copying the backup over the file here would race the open
                // connection, so the backup stays on disk for manual recovery only. The app keeps one context for
                // its whole session, so the rolled-back entities must also leave the change tracker.
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                throw;
            }
        }

        private ExportModel ParseAndValidateImportJson(string json)
        {
            ExportModel? model;
            try
            {
                model = JsonSerializer.Deserialize<ExportModel>(json, _jsonOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(Strings.Import_InvalidJson, ex);
            }

            if (model == null)
                throw new InvalidOperationException(Strings.Import_InvalidJson);

            ValidateModel(model);
            return model;
        }

        private static void ValidateModel(ExportModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SchemaVersion))
                throw new InvalidOperationException(Strings.Import_MissingSchemaVersion);

            if (!string.Equals(model.SchemaVersion, "1", StringComparison.Ordinal))
                throw new InvalidOperationException(string.Format(Strings.Import_UnsupportedSchema, model.SchemaVersion));

            if (model.Categories.Any(c => string.IsNullOrWhiteSpace(c.Name)))
                throw new InvalidOperationException(Strings.Import_MissingCategoryName);

            if (model.Occasions.Any(o => string.IsNullOrWhiteSpace(o.Title)))
                throw new InvalidOperationException(Strings.Import_MissingOccasionTitle);

            if (model.Milestones.Any(m => string.IsNullOrWhiteSpace(m.Label)))
                throw new InvalidOperationException(Strings.Import_MissingMilestoneLabel);
        }

        private static string? GetSqliteDataSource(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            // crude parse for 'Data Source=' key
            var parts = connectionString.Split(';');
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }
            return null;
        }
    }
}
