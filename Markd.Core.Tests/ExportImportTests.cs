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
    public class ExportImportTests
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
        public async Task ExportAndImport_RoundTrip_Works()
        {
            // Seed source DB (in-memory file)
            var srcPath = Path.Combine(Path.GetTempPath(), $"markd_src_{Guid.NewGuid():N}.db");
            using (var src = CreateSqliteContext(srcPath))
            {
                src.Categories.Add(new Markd.Core.Domain.Category { Name = "Family", Emoji = "👪" });
                await src.SaveChangesAsync();

                var cat = src.Categories.First();
                var occ = new Markd.Core.Domain.Occasion
                {
                    Title = "Anniversary",
                    AnchorDate = new DateTime(2020, 1, 1),
                    Direction = Markd.Core.Domain.OccasionDirection.Since,
                    CategoryId = cat.Id,
                };
                src.Occasions.Add(occ);
                await src.SaveChangesAsync();

                var milestone = new Markd.Core.Domain.Milestone
                {
                    OccasionId = occ.Id,
                    ThresholdDays = 365,
                    Label = "1 year",
                    Notified = false
                };
                src.Milestones.Add(milestone);

                src.AppSettings.Add(new Markd.Core.Domain.AppSettings { Theme = "Light", Language = "en", NotificationsEnabled = true });
                await src.SaveChangesAsync();

                var exporter = new ExportService(src);
                var package = await exporter.CreateExportPackageAsync();

                // Import into new DB
                var tgtPath = Path.Combine(Path.GetTempPath(), $"markd_tgt_{Guid.NewGuid():N}.db");
                using (var tgt = CreateSqliteContext(tgtPath))
                {
                    var importer = new ImportService(tgt);
                    var model = await importer.ParseImportPackageAsync(package);
                    await importer.ApplyImportAsync(model);

                    // Validate
                    Assert.Equal(1, await tgt.Categories.CountAsync());
                    Assert.Equal(1, await tgt.Occasions.CountAsync());
                    Assert.Equal(1, await tgt.Milestones.CountAsync());
                    var settings = await tgt.AppSettings.FirstOrDefaultAsync();
                    Assert.NotNull(settings);
                    Assert.Equal("Light", settings.Theme);
                }
            }
        }
    }
}
