using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Markd.Core.Data;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests
{
    public class ExportImportEncryptedTests
    {
        private MarkdDbContext CreateSqliteContext(string dbPath)
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            var options = new DbContextOptionsBuilder<MarkdDbContext>()
                .UseSqlite(cs)
                .Options;
            var db = new MarkdDbContext(options);
            db.Database.EnsureCreated();
            return db;
        }

        [Fact]
        public async Task EncryptedExport_Import_RoundTrip_Works()
        {
            var pass = "s3cr3t-passphrase";

            var srcPath = Path.Combine(Path.GetTempPath(), $"markd_src_enc_{Guid.NewGuid():N}.db");
            using (var src = CreateSqliteContext(srcPath))
            {
                src.Categories.Add(new Markd.Core.Domain.Category { Name = "Friends", Emoji = "🙂" });
                await src.SaveChangesAsync();

                var cat = src.Categories.First();
                var occ = new Markd.Core.Domain.Occasion
                {
                    Title = "Birthday",
                    AnchorDate = new DateTime(2021, 6, 1),
                    Direction = Markd.Core.Domain.OccasionDirection.Since,
                    CategoryId = cat.Id,
                };
                src.Occasions.Add(occ);
                await src.SaveChangesAsync();

                var settings = await src.AppSettings.FirstAsync();
                settings.Theme = "Dark";
                settings.Language = "cs";
                settings.NotificationsEnabled = false;
                await src.SaveChangesAsync();

                var exporter = new ExportService(src);
                var package = await exporter.CreateExportPackageAsync(pass);

                Assert.True(MarkdPackage.IsEncrypted(package));

                var tgtPath = Path.Combine(Path.GetTempPath(), $"markd_tgt_enc_{Guid.NewGuid():N}.db");
                using (var tgt = CreateSqliteContext(tgtPath))
                {
                    var importer = new ImportService(tgt);
                    var model = await importer.ParseImportPackageAsync(package, pass);
                    await importer.ApplyImportAsync(model);

                    Assert.Equal(1, await tgt.Categories.CountAsync());
                    Assert.Equal(1, await tgt.Occasions.CountAsync());
                    var importedSettings = await tgt.AppSettings.FirstOrDefaultAsync();
                    Assert.NotNull(importedSettings);
                    Assert.Equal("Dark", importedSettings.Theme);
                }
            }
        }

        [Fact]
        public async Task EncryptedImport_WrongPassphrase_ThrowsWrongPassword()
        {
            var srcPath = Path.Combine(Path.GetTempPath(), $"markd_src_wrongpass_{Guid.NewGuid():N}.db");
            using var src = CreateSqliteContext(srcPath);
            src.Categories.Add(new Markd.Core.Domain.Category { Name = "Friends", Emoji = "🙂" });
            await src.SaveChangesAsync();

            var exporter = new ExportService(src);
            var package = await exporter.CreateExportPackageAsync("s3cr3t-passphrase");

            var tgtPath = Path.Combine(Path.GetTempPath(), $"markd_tgt_wrongpass_{Guid.NewGuid():N}.db");
            using var tgt = CreateSqliteContext(tgtPath);
            var importer = new ImportService(tgt);

            var ex = await Assert.ThrowsAsync<MarkdPackageException>(() => importer.ParseImportPackageAsync(package, "not the right passphrase"));
            Assert.Equal(PackageError.WrongPassword, ex.Error);
        }

        [Fact]
        public async Task Import_Twice_ReplacesExistingData()
        {
            var srcPath = Path.Combine(Path.GetTempPath(), $"markd_src_dup_{Guid.NewGuid():N}.db");
            using (var src = CreateSqliteContext(srcPath))
            {
                src.Categories.Add(new Markd.Core.Domain.Category { Name = "Work", Emoji = "💼" });
                await src.SaveChangesAsync();

                var cat = src.Categories.First();
                var occ = new Markd.Core.Domain.Occasion
                {
                    Title = "Project Start",
                    AnchorDate = new DateTime(2022, 1, 10),
                    Direction = Markd.Core.Domain.OccasionDirection.Since,
                    CategoryId = cat.Id,
                };
                src.Occasions.Add(occ);
                await src.SaveChangesAsync();

                var exporter = new ExportService(src);
                var package = await exporter.CreateExportPackageAsync();

                var tgtPath = Path.Combine(Path.GetTempPath(), $"markd_tgt_dup_{Guid.NewGuid():N}.db");
                using (var tgt = CreateSqliteContext(tgtPath))
                {
                    var importer = new ImportService(tgt);
                    var model = await importer.ParseImportPackageAsync(package);
                    await importer.ApplyImportAsync(model);

                    // Apply the same model again
                    await importer.ApplyImportAsync(model);

                    Assert.Equal(1, await tgt.Categories.CountAsync());
                    Assert.Equal(1, await tgt.Occasions.CountAsync());
                    Assert.Equal("Project Start", await tgt.Occasions.Select(x => x.Title).SingleAsync());
                }
            }
        }
    }
}
