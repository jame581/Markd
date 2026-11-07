using System.Text;
using Markd.Core.Domain;
using Markd.Core.Services;
using Xunit;

namespace Markd.Core.Tests;

public class ExportModelV2Tests
{
    [Fact]
    public async Task Export_writes_schema_2_with_sync_ids_and_omits_tombstones()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var occasions = new OccasionService(db);
        var categories = new CategoryService(db);
        var cat = await categories.CreateAsync(new Category { Name = "Family" });
        var live = await occasions.CreateAsync(new Occasion { Title = "Live", AnchorDate = DateTime.UtcNow, CategoryId = cat.Id });
        await occasions.AddMilestoneAsync(live.Id, 5, "five");
        var dead = await occasions.CreateAsync(new Occasion { Title = "Dead", AnchorDate = DateTime.UtcNow });
        await occasions.DeleteAsync(dead.Id);

        var bytes = await new ExportService(db).CreateExportJsonAsync();
        var model = ExportModelJson.Deserialize(Encoding.UTF8.GetString(bytes));

        Assert.Equal("2", model.SchemaVersion);
        var o = Assert.Single(model.Occasions);
        Assert.Equal(live.SyncId, o.SyncId);
        Assert.Equal(cat.SyncId, o.CategorySyncId);
        Assert.Equal(live.SyncId, Assert.Single(model.Milestones).OccasionSyncId);
        Assert.NotNull(model.Settings);
    }

    [Fact]
    public void Upgrade_gives_v1_records_fresh_ids_and_rewrites_references()
    {
        var v1 = new ExportModel
        {
            SchemaVersion = "1",
            Categories = { new CategoryDto { SourceId = 7, Name = "C" } },
            Occasions = { new OccasionDto { SourceId = 3, Title = "O", CategorySourceId = 7, CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) } },
            Milestones = { new MilestoneDto { SourceId = 9, OccasionSourceId = 3, Label = "M", ThresholdDays = 1 } },
        };

        var v2 = ExportModelUpgrade.ToCurrent(v1);

        Assert.Equal("2", v2.SchemaVersion);
        var cat = Assert.Single(v2.Categories);
        var occ = Assert.Single(v2.Occasions);
        var ms = Assert.Single(v2.Milestones);
        Assert.NotEqual(Guid.Empty, cat.SyncId);
        Assert.Equal(cat.SyncId, occ.CategorySyncId);
        Assert.Equal(occ.SyncId, ms.OccasionSyncId);
        Assert.Equal(occ.CreatedAt, occ.UpdatedAt);
        Assert.Null(occ.DeletedAt);
        Assert.Equal("1", v1.SchemaVersion); // input untouched
    }

    [Fact]
    public void Deserialize_accepts_1_and_2_and_rejects_others()
    {
        Assert.Equal("1", ExportModelJson.Deserialize("{\"schemaVersion\":\"1\"}").SchemaVersion);
        Assert.Equal("2", ExportModelJson.Deserialize("{\"schemaVersion\":\"2\"}").SchemaVersion);
        Assert.Throws<InvalidOperationException>(() => ExportModelJson.Deserialize("{\"schemaVersion\":\"3\"}"));
        Assert.Throws<InvalidOperationException>(() => ExportModelJson.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_rejects_duplicate_or_empty_sync_ids_in_v2()
    {
        var id = Guid.NewGuid();
        var dup = new ExportModel
        {
            SchemaVersion = "2",
            Categories = { new CategoryDto { SyncId = id, Name = "a" }, new CategoryDto { SyncId = id, Name = "b" } },
        };
        Assert.Throws<InvalidOperationException>(() => ExportModelJson.Deserialize(ExportModelJson.Serialize(dup, false)));

        var empty = new ExportModel { SchemaVersion = "2", Categories = { new CategoryDto { Name = "a" } } };
        Assert.Throws<InvalidOperationException>(() => ExportModelJson.Deserialize(ExportModelJson.Serialize(empty, false)));
    }

    [Fact]
    public async Task A_v2_snapshot_carries_the_local_calendar_day()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var tokyo = Zones.Utc(9);
        await new OccasionService(db).CreateAsync(new Occasion
        {
            Title = "Birthday", AnchorDate = new DateTime(2026, 3, 1, 15, 0, 0, DateTimeKind.Utc),   // local midnight of 2 March in UTC+9
        });

        var model = await new SnapshotService(db, tokyo).ReadForSyncAsync();
        var json = ExportModelJson.Serialize(model, indented: false);
        var exported = Encoding.UTF8.GetString(await new ExportService(db, tokyo).CreateExportJsonAsync());

        Assert.Equal(new DateOnly(2026, 3, 2), Assert.Single(model.Occasions).AnchorDay);
        Assert.Contains("\"anchorDay\":\"2026-03-02\"", json);
        Assert.DoesNotContain("anchorDate", json);
        Assert.Contains("\"anchorDay\": \"2026-03-02\"", exported);
        Assert.DoesNotContain("anchorDate", exported);
    }

    [Fact]
    public void A_v1_file_imports_as_the_day_this_device_shows()
    {
        var v1 = ExportModelJson.Deserialize(
            "{\"schemaVersion\":\"1\",\"occasions\":[{\"sourceId\":1,\"title\":\"Birthday\",\"anchorDate\":\"2026-03-01T15:00:00Z\",\"direction\":\"Since\"}]}");

        var v2 = ExportModelUpgrade.ToCurrent(v1, Zones.Utc(9));

        Assert.Equal(new DateOnly(2026, 3, 2), Assert.Single(v2.Occasions).AnchorDay);
        Assert.DoesNotContain("anchorDate", ExportModelJson.Serialize(v2, indented: false));
    }

    [Fact]
    public void A_v2_file_without_a_day_is_rejected()
    {
        var json = "{\"schemaVersion\":\"2\",\"occasions\":[{\"syncId\":\"" + Guid.NewGuid() +
                   "\",\"title\":\"Birthday\",\"anchorDate\":\"2026-03-01T15:00:00Z\",\"direction\":\"Since\"}]}";
        Assert.Throws<InvalidOperationException>(() => ExportModelJson.Deserialize(json));

        var model = new ExportModel { Occasions = { new OccasionDto { SyncId = Guid.NewGuid(), Title = "Birthday", Direction = "Since" } } };
        var package = Markd.Core.Sync.SyncSnapshotCodec.Encode(model, "pass", MarkdPackage.MinIterations);
        Assert.Throws<Markd.Core.Sync.SnapshotFormatException>(() => Markd.Core.Sync.SyncSnapshotCodec.Decode(package, "pass"));
    }

    [Fact]
    public void CloneModel_is_deep_for_records()
    {
        var m = new ExportModel { Occasions = { new OccasionDto { SyncId = Guid.NewGuid(), Title = "a" } } };
        var c = m.CloneModel();
        c.Occasions[0].Title = "changed";
        Assert.Equal("a", m.Occasions[0].Title);
    }
}
