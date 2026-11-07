using Markd.Core.Domain;
using Markd.Core.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SnapshotServiceTests
{
    private static readonly DateTime T = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static (SnapshotService Snap, OccasionService Occ, CategoryService Cat, Data.MarkdDbContext Db, IDisposable Conn) Create()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        return (new SnapshotService(db), new OccasionService(db), new CategoryService(db), db, conn);
    }

    [Fact]
    public async Task ReadForSync_includes_tombstones_and_hides_Notified()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var live = await occ.CreateAsync(new Occasion { Title = "live", AnchorDate = DateTime.UtcNow.AddDays(-9) });
        var m = await occ.AddMilestoneAsync(live.Id, 1, "one");
        await occ.MarkMilestoneNotifiedAsync(m.Id);
        var dead = await occ.CreateAsync(new Occasion { Title = "dead", AnchorDate = DateTime.UtcNow });
        await occ.DeleteAsync(dead.Id);

        var model = await snap.ReadForSyncAsync();

        Assert.Equal(2, model.Occasions.Count);
        Assert.Contains(model.Occasions, o => o.DeletedAt != null);
        Assert.False(Assert.Single(model.Milestones).Notified);
    }

    [Fact]
    public async Task Apply_inserts_updates_and_tombstones_by_SyncId_and_keeps_local_Notified()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var keep = await occ.CreateAsync(new Occasion { Title = "keep", AnchorDate = DateTime.UtcNow.AddDays(-9) });
        var ms = await occ.AddMilestoneAsync(keep.Id, 1, "one");
        await occ.MarkMilestoneNotifiedAsync(ms.Id);
        var drop = await occ.CreateAsync(new Occasion { Title = "drop", AnchorDate = DateTime.UtcNow });

        var target = await snap.ReadForSyncAsync();
        target.Occasions.RemoveAll(o => o.SyncId == drop.SyncId);
        target.Occasions.Single(o => o.SyncId == keep.SyncId).Title = "renamed";
        target.Occasions.Add(new OccasionDto { SyncId = Guid.NewGuid(), Title = "new", AnchorDay = DateOnly.FromDateTime(T), Direction = "Since", UpdatedAt = T });

        await snap.ApplyAsync(target, new SnapshotApplyOptions(TakeNotifiedFromTarget: false, NowUtc: T));

        var live = await occ.GetAllAsync();
        Assert.Equal(new[] { "new", "renamed" }, live.Select(o => o.Title).OrderBy(t => t));
        var renamed = live.Single(o => o.Title == "renamed");
        Assert.True(renamed.Milestones.Single().Notified);
        var dropped = await db.Occasions.IgnoreQueryFilters().SingleAsync(o => o.SyncId == drop.SyncId);
        Assert.Equal(T, dropped.DeletedAt);
    }

    [Fact]
    public async Task Apply_stores_UpdatedAt_from_the_target_instead_of_stamping()
    {
        var (snap, _, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var target = new ExportModel { Categories = { new CategoryDto { SyncId = Guid.NewGuid(), Name = "c", UpdatedAt = T } } };

        await snap.ApplyAsync(target, new SnapshotApplyOptions(false));

        Assert.Equal(T, (await db.Categories.SingleAsync()).UpdatedAt);
        Assert.True(db.StampTimestamps);
    }

    [Fact]
    public async Task Apply_moves_the_pin_without_violating_the_unique_pin_index()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var a = new Occasion { Title = "a", AnchorDate = DateTime.UtcNow, IsPinned = true };
        var b = new Occasion { Title = "b", AnchorDate = DateTime.UtcNow };
        await occ.CreateAsync(a);
        await occ.CreateAsync(b);

        var target = await snap.ReadForSyncAsync();
        target.Occasions.Single(o => o.SyncId == a.SyncId).IsPinned = false;
        target.Occasions.Single(o => o.SyncId == b.SyncId).IsPinned = true;
        await snap.ApplyAsync(target, new SnapshotApplyOptions(false));

        var pinned = (await occ.GetAllAsync()).Single(o => o.IsPinned);
        Assert.Equal("b", pinned.Title);
    }

    [Fact]
    public async Task Apply_moves_the_pin_to_a_lower_Id_without_violating_the_unique_pin_index()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var a = await occ.CreateAsync(new Occasion { Title = "a", AnchorDate = DateTime.UtcNow });
        var b = await occ.CreateAsync(new Occasion { Title = "b", AnchorDate = DateTime.UtcNow, IsPinned = true });
        Assert.True(a.Id < b.Id);

        var target = await snap.ReadForSyncAsync();
        target.Occasions.Single(o => o.SyncId == a.SyncId).IsPinned = true;
        target.Occasions.Single(o => o.SyncId == b.SyncId).IsPinned = false;
        await snap.ApplyAsync(target, new SnapshotApplyOptions(false));

        Assert.Equal("a", Assert.Single(await occ.GetAllAsync(), o => o.IsPinned).Title);
    }

    [Fact]
    public async Task Apply_remaps_local_SyncIds_in_place_and_keeps_Notified()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var o = await occ.CreateAsync(new Occasion { Title = "same", AnchorDate = DateTime.UtcNow.AddDays(-9) });
        var m = await occ.AddMilestoneAsync(o.Id, 1, "one");
        await occ.MarkMilestoneNotifiedAsync(m.Id);
        var remoteOccasionId = Guid.NewGuid();
        var remoteMilestoneId = Guid.NewGuid();
        var local = await snap.ReadForSyncAsync();
        var target = local.CloneModel();
        target.Occasions[0].SyncId = remoteOccasionId;
        target.Milestones[0].SyncId = remoteMilestoneId;
        target.Milestones[0].OccasionSyncId = remoteOccasionId;
        var remaps = new Dictionary<Guid, Guid> { [o.SyncId] = remoteOccasionId, [m.SyncId] = remoteMilestoneId };

        await snap.ApplyAsync(target, new SnapshotApplyOptions(false, SyncIdRemaps: remaps));

        var all = await db.Occasions.IgnoreQueryFilters().Include(x => x.Milestones).ToListAsync();
        var only = Assert.Single(all);
        Assert.Equal(o.Id, only.Id);
        Assert.Equal(remoteOccasionId, only.SyncId);
        Assert.True(Assert.Single(only.Milestones).Notified);
    }

    [Fact]
    public async Task Apply_can_take_Notified_and_settings_from_the_target()
    {
        var (snap, _, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var occ = Guid.NewGuid();
        var target = new ExportModel
        {
            Occasions = { new OccasionDto { SyncId = occ, Title = "o", AnchorDay = DateOnly.FromDateTime(T), Direction = "Since" } },
            Milestones = { new MilestoneDto { SyncId = Guid.NewGuid(), OccasionSyncId = occ, Label = "m", ThresholdDays = 1, Notified = true } },
            Settings = new AppSettingsDto { Theme = "Dark", Language = "cs", NotificationsEnabled = false },
        };

        await snap.ApplyAsync(target, new SnapshotApplyOptions(TakeNotifiedFromTarget: true, ApplySettings: true));

        Assert.True((await db.Milestones.SingleAsync()).Notified);
        var settings = await db.AppSettings.SingleAsync();
        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("cs", settings.Language);
        Assert.False(settings.NotificationsEnabled);
    }

    [Fact]
    public async Task Apply_rolls_back_on_failure_and_leaves_local_data_alone()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        await occ.CreateAsync(new Occasion { Title = "safe", AnchorDate = DateTime.UtcNow });
        var target = new ExportModel
        {
            Occasions =
            {
                new OccasionDto { SyncId = Guid.NewGuid(), Title = "x", AnchorDay = DateOnly.FromDateTime(T), Direction = "Since", IsPinned = true },
                new OccasionDto { SyncId = Guid.NewGuid(), Title = "y", AnchorDay = DateOnly.FromDateTime(T), Direction = "Since", IsPinned = true }, // two pins: violates the unique index
            }
        };

        await Assert.ThrowsAnyAsync<Exception>(() => snap.ApplyAsync(target, new SnapshotApplyOptions(false)));

        Assert.Equal(new[] { "safe" }, (await occ.GetAllAsync()).Select(o => o.Title));
        Assert.True(db.StampTimestamps);
    }

    [Fact]
    public async Task Apply_aborts_without_changes_when_local_data_changed_since_it_was_read()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        await occ.CreateAsync(new Occasion { Title = "before", AnchorDate = DateTime.UtcNow });
        var read = await snap.ReadForSyncAsync();
        var fingerprint = Markd.Core.Sync.SyncContent.Fingerprint(read);
        await occ.CreateAsync(new Occasion { Title = "typed meanwhile", AnchorDate = DateTime.UtcNow });

        await Assert.ThrowsAsync<SnapshotChangedException>(
            () => snap.ApplyAsync(read, new SnapshotApplyOptions(false, ExpectedLocalFingerprint: fingerprint)));

        Assert.Equal(new[] { "before", "typed meanwhile" }, (await occ.GetAllAsync()).Select(o => o.Title).OrderBy(t => t));
        Assert.True(db.StampTimestamps);
    }

    [Fact]
    public async Task Apply_marks_inserted_milestones_that_are_already_reached_as_notified()
    {
        var (snap, _, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var occasionId = Guid.NewGuid();
        var target = new ExportModel
        {
            Occasions = { new OccasionDto { SyncId = occasionId, Title = "o", AnchorDay = DateOnly.FromDateTime(DateTime.Today.AddDays(-10)), Direction = "Since", UpdatedAt = T } },
            Milestones =
            {
                new MilestoneDto { SyncId = Guid.NewGuid(), OccasionSyncId = occasionId, Label = "reached", ThresholdDays = 5, UpdatedAt = T },
                new MilestoneDto { SyncId = Guid.NewGuid(), OccasionSyncId = occasionId, Label = "future", ThresholdDays = 100, UpdatedAt = T },
            },
        };

        await snap.ApplyAsync(target, new SnapshotApplyOptions(TakeNotifiedFromTarget: false));

        var milestones = await db.Milestones.ToListAsync();
        Assert.True(milestones.Single(m => m.ThresholdDays == 5).Notified);
        Assert.False(milestones.Single(m => m.ThresholdDays == 100).Notified);
    }

    private static async Task<DateTime> ApplyNewDayAsync(TimeZoneInfo zone, DateOnly day)
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var target = new ExportModel
        {
            Occasions = { new OccasionDto { SyncId = Guid.NewGuid(), Title = "Birthday", AnchorDay = day, Direction = "Since", UpdatedAt = T } },
        };

        await new SnapshotService(db, zone).ApplyAsync(target, new SnapshotApplyOptions(false));

        return DateTime.SpecifyKind((await db.Occasions.AsNoTracking().SingleAsync()).AnchorDate, DateTimeKind.Utc);
    }

    [Fact]
    public async Task Applying_a_day_stores_local_midnight_of_this_device()
    {
        var zone = Zones.Utc(-5);

        var stored = await ApplyNewDayAsync(zone, new DateOnly(2026, 3, 2));

        Assert.Equal(new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc), stored);
        Assert.Equal(new DateTime(2026, 3, 2), OccasionDates.ToLocalDate(stored, zone));
    }

    [Fact]
    public async Task Applying_a_day_whose_midnight_does_not_exist_stores_the_first_valid_time_after_it()
    {
        var zone = Zones.MidnightGap();   // 2 March jumps from 00:00 (UTC-5) to 01:00 (UTC-4)

        var stored = await ApplyNewDayAsync(zone, new DateOnly(2026, 3, 2));

        Assert.Equal(new DateTime(2026, 3, 2, 5, 0, 0, DateTimeKind.Utc), stored);   // 01:00 local
        Assert.Equal(new DateTime(2026, 3, 2), OccasionDates.ToLocalDate(stored, zone));
    }

    [Fact]
    public async Task Apply_with_the_same_stored_day_leaves_the_instant_and_Notified_alone()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var occ = new OccasionService(db, Zones.Utc(1));
        var pragueMidnight = new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc);   // 2 March in UTC+1, 1 March in UTC-5
        var o = await occ.CreateAsync(new Occasion { Title = "o", AnchorDate = pragueMidnight });
        var m = await occ.AddMilestoneAsync(o.Id, 1000, "far");
        await occ.MarkMilestoneNotifiedAsync(m.Id);
        var snap = new SnapshotService(db, Zones.Utc(-5));   // the device has since travelled west
        var target = await snap.ReadForSyncAsync();
        target.Occasions[0].AnchorDay = new DateOnly(2026, 3, 2);
        target.Occasions[0].Notes = "edited elsewhere";

        await snap.ApplyAsync(target, new SnapshotApplyOptions(false));

        var stored = await db.Occasions.AsNoTracking().SingleAsync();
        Assert.Equal(pragueMidnight, DateTime.SpecifyKind(stored.AnchorDate, DateTimeKind.Utc));
        Assert.Equal(new DateOnly(2026, 3, 2), stored.AnchorDay);
        Assert.True((await db.Milestones.SingleAsync()).Notified);
    }

    [Fact]
    public async Task Apply_judges_arriving_milestones_at_the_apply_time()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var occasionId = Guid.NewGuid();
        var target = new ExportModel
        {
            Occasions = { new OccasionDto { SyncId = occasionId, Title = "o", AnchorDay = new DateOnly(2026, 3, 2), Direction = "Since", UpdatedAt = T } },
            Milestones = { new MilestoneDto { SyncId = Guid.NewGuid(), OccasionSyncId = occasionId, Label = "far", ThresholdDays = 1000, UpdatedAt = T } },
        };

        await new SnapshotService(db, Zones.Utc(0)).ApplyAsync(target,
            new SnapshotApplyOptions(false, NowUtc: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.True((await db.Milestones.SingleAsync()).Notified);
    }

    private static async Task<(SnapshotService Snap, Data.MarkdDbContext Db, IDisposable Conn, ExportModel Target, Occasion O, Milestone M)> Seed(int anchorDaysAgo, int threshold, bool notified)
    {
        var (snap, occ, _, db, conn) = Create();
        var o = await occ.CreateAsync(new Occasion { Title = "o", AnchorDate = DateTime.UtcNow.AddDays(-anchorDaysAgo) });
        var m = await occ.AddMilestoneAsync(o.Id, threshold, "m");
        if (notified) await occ.MarkMilestoneNotifiedAsync(m.Id);
        return (snap, db, conn, await snap.ReadForSyncAsync(), o, m);
    }

    private static async Task<bool> NotifiedAfter(SnapshotService snap, Data.MarkdDbContext db, ExportModel target)
    {
        await snap.ApplyAsync(target, new SnapshotApplyOptions(false));
        return (await db.Milestones.SingleAsync()).Notified;
    }

    [Fact]
    public async Task Apply_marks_an_existing_milestone_notified_when_the_anchor_moves_into_the_past()
    {
        var (snap, db, conn, target, _, _) = await Seed(1, 5, false);
        using var _ = conn; using var __ = db;
        target.Occasions[0].AnchorDay = DateOnly.FromDateTime(DateTime.Today.AddDays(-20));
        Assert.True(await NotifiedAfter(snap, db, target));
    }

    [Fact]
    public async Task Apply_clears_Notified_of_an_existing_milestone_when_the_anchor_moves_into_the_future()
    {
        var (snap, db, conn, target, _, _) = await Seed(20, 5, true);
        using var _ = conn; using var __ = db;
        target.Occasions[0].AnchorDay = DateOnly.FromDateTime(DateTime.Today);
        Assert.False(await NotifiedAfter(snap, db, target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Apply_keeps_local_Notified_when_only_the_title_changes(bool notified)
    {
        var (snap, db, conn, target, _, _) = await Seed(20, 5, notified);
        using var _ = conn; using var __ = db;
        target.Occasions[0].Title = "renamed";
        Assert.Equal(notified, await NotifiedAfter(snap, db, target));
    }

    [Fact]
    public async Task Apply_recomputes_Notified_when_the_threshold_changes()
    {
        var (snap, db, conn, target, _, _) = await Seed(20, 100, false);
        using var _ = conn; using var __ = db;
        target.Milestones[0].ThresholdDays = 5;
        Assert.True(await NotifiedAfter(snap, db, target));
    }

    [Fact]
    public async Task Purge_removes_only_old_tombstones()
    {
        var (snap, occ, _, db, conn) = Create();
        using var _ = conn; using var __ = db;
        var old = await occ.CreateAsync(new Occasion { Title = "old", AnchorDate = DateTime.UtcNow });
        var recent = await occ.CreateAsync(new Occasion { Title = "recent", AnchorDate = DateTime.UtcNow });
        await occ.CreateAsync(new Occasion { Title = "live", AnchorDate = DateTime.UtcNow });
        await occ.DeleteAsync(old.Id);
        await occ.DeleteAsync(recent.Id);
        db.StampTimestamps = false;
        (await db.Occasions.IgnoreQueryFilters().SingleAsync(o => o.Id == old.Id)).DeletedAt = T.AddDays(-200);
        (await db.Occasions.IgnoreQueryFilters().SingleAsync(o => o.Id == recent.Id)).DeletedAt = T.AddDays(-5);
        await db.SaveChangesAsync();
        db.StampTimestamps = true;

        var removed = await snap.PurgeTombstonesAsync(T.AddDays(-90));

        Assert.Equal(1, removed);
        Assert.Equal(2, await db.Occasions.IgnoreQueryFilters().CountAsync());
    }
}
