using Markd.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Markd.Core.Tests;

public class MigrationBackfillTests
{
    [Fact]
    public async Task Existing_rows_get_unique_SyncIds_and_a_real_UpdatedAt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"markd_mig_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<MarkdDbContext>().UseSqlite($"Data Source={path}").Options;
            await using (var db = new MarkdDbContext(options))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync("20260918214035_DefaultLanguageSystem", TestContext.Current.CancellationToken);

                // Adjust this column list if a NOT NULL column of the 1.0 schema is missing.
                await db.Database.ExecuteSqlRawAsync("INSERT INTO Categories (Name) VALUES ('A'), ('B')", TestContext.Current.CancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Occasions (Title, IsPinned, AnchorDate, Direction, CreatedAt) VALUES " +
                    "('O1', 0, '2020-01-01 00:00:00', 'Since', '2020-01-02 00:00:00'), " +
                    "('O2', 0, '2021-01-01 00:00:00', 'Until', '2021-01-02 00:00:00')", TestContext.Current.CancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Milestones (OccasionId, ThresholdDays, Label, Notified) VALUES (1, 10, 'M1', 0), (1, 20, 'M2', 0)", TestContext.Current.CancellationToken);
            }

            await using (var db = new MarkdDbContext(options))
            {
                await db.Database.MigrateAsync(TestContext.Current.CancellationToken);

                var categories = await db.Categories.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
                var occasions = await db.Occasions.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
                var milestones = await db.Milestones.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);

                var ids = categories.Select(c => c.SyncId).Concat(occasions.Select(o => o.SyncId)).Concat(milestones.Select(m => m.SyncId)).ToList();
                Assert.Equal(6, ids.Count);
                Assert.DoesNotContain(Guid.Empty, ids);
                Assert.Equal(ids.Count, ids.Distinct().Count());

                Assert.All(occasions, o => Assert.Equal(o.CreatedAt, o.UpdatedAt));
                Assert.All(categories, c => Assert.True(c.UpdatedAt.Year >= 2026));
                Assert.All(milestones, m => Assert.True(m.UpdatedAt.Year >= 2026));
                Assert.All(occasions, o => Assert.Null(o.DeletedAt));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void The_SyncId_backfill_survives_the_smallest_random_value()
    {
        // SQLite's random() can return INT64_MIN, the one value abs() rejects with "integer overflow".
        var backfills = new Markd.Core.Data.Migrations.AddSync().UpOperations
            .OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()
            .Where(o => o.Sql.Contains("\"SyncId\""))
            .ToList();
        Assert.Equal(3, backfills.Count);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE Categories (SyncId TEXT); CREATE TABLE Occasions (SyncId TEXT); " +
                                 "CREATE TABLE Milestones (SyncId TEXT); INSERT INTO Categories VALUES (NULL); " +
                                 "INSERT INTO Occasions VALUES (NULL); INSERT INTO Milestones VALUES (NULL);";
            create.ExecuteNonQuery();
        }

        foreach (var backfill in backfills)
        {
            using var command = connection.CreateCommand();
            command.CommandText = backfill.Sql.Replace("random()", "(-9223372036854775807 - 1)");
            command.ExecuteNonQuery();
        }

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT SyncId FROM Categories UNION ALL SELECT SyncId FROM Occasions UNION ALL SELECT SyncId FROM Milestones";
        using var reader = read.ExecuteReader();
        while (reader.Read())
        {
            var id = Guid.Parse(reader.GetString(0));
            Assert.Contains(id.ToString()[19], "89ab");
        }
    }
}
