using System.Text;
using Markd.Core.Services;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class ImportAdditiveTests
{
    private static async Task<ExportModel> BackupAsync(SyncDevice device) =>
        ExportModelJson.Deserialize(Encoding.UTF8.GetString(await new ExportService(device.Db, device.Zone).CreateExportJsonAsync()));

    private static Task<ImportOutcome> RestoreAsync(SyncDevice device, ExportModel backup) =>
        new ImportService(device.Db, device.Snapshots, device.State).ApplyImportAsync(backup);

    private static async Task<int> CountAsync(SyncDevice device, string title) =>
        (await device.TitlesAsync()).Count(t => t == title);

    private static async Task SyncedAsync(SyncDevice device, InMemoryCloudProvider cloud)
    {
        var result = await device.Sync.SyncAsync(cloud);
        Assert.True(result.Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges, $"sync ended with {result.Outcome}");
    }

    [Fact]
    public async Task Restore_on_a_synced_device_keeps_events_added_after_the_backup()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        var backup = await BackupAsync(a);
        await b.AddOccasionAsync("Y");
        await SyncedAsync(b, cloud);
        await SyncedAsync(a, cloud);

        var outcome = await RestoreAsync(a, backup);

        Assert.True(outcome.Additive);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        Assert.Equal(new[] { "X", "Y" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Restore_keeps_the_current_version_of_an_event_edited_after_the_backup()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        var backup = await BackupAsync(a);
        var edit = (await a.Occasions.GetByIdAsync(x.Id))!;
        edit.Title = "X renamed";
        await a.Occasions.UpdateAsync(edit);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);

        var outcome = await RestoreAsync(a, backup);

        Assert.Equal(new ImportOutcome(true, 0, 0, 1), outcome);
        Assert.Equal(new[] { "X renamed" }, await a.TitlesAsync());
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        Assert.Equal(new[] { "X renamed" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Restore_brings_back_an_event_deleted_after_the_backup()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.AddOccasionAsync("Y");
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        var backup = await BackupAsync(a);
        await a.Occasions.DeleteAsync(x.Id);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        Assert.Equal(new[] { "Y" }, await b.TitlesAsync());

        var outcome = await RestoreAsync(a, backup);

        Assert.Equal(new ImportOutcome(true, 0, 1, 1), outcome);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());
        Assert.Single((await a.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        Assert.Equal(new[] { "X", "Y" }, await b.TitlesAsync());
        Assert.Single((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }

    [Fact]
    public async Task Restore_never_doubles_an_unchanged_event()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        var backup = await BackupAsync(a);

        await RestoreAsync(a, backup);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);

        Assert.Equal(1, await CountAsync(a, "X"));
        Assert.Equal(1, await CountAsync(b, "X"));
        Assert.Single((await a.Occasions.GetAllAsync()).Single().Milestones);
        Assert.Single((await b.Occasions.GetAllAsync()).Single().Milestones);
    }

    [Fact]
    public async Task A_pre_first_sync_v2_backup_does_not_double_aligned_events()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var day = new DateOnly(2024, 6, 1);
        var wedding = await a.AddOccasionOnAsync("Wedding", day);
        await a.Occasions.AddMilestoneAsync(wedding.Id, 100, "100 days");
        await b.AddOccasionOnAsync("Wedding", day);
        await b.Occasions.AddMilestoneAsync((await b.Occasions.GetAllAsync()).Single().Id, 100, "100 days");
        var backup = await BackupAsync(a);
        await SyncedAsync(b, cloud);
        await SyncedAsync(a, cloud);   // the first sync re-identifies A's Wedding with B's SyncId
        Assert.NotEqual(backup.Occasions.Single().SyncId, (await a.Occasions.GetAllAsync()).Single().SyncId);

        var outcome = await RestoreAsync(a, backup);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);

        Assert.Equal(new ImportOutcome(true, 0, 0, 1), outcome);
        Assert.Single(Assert.Single(await a.Occasions.GetAllAsync()).Milestones);
        Assert.Single(Assert.Single(await b.Occasions.GetAllAsync()).Milestones);
    }

    [Fact]
    public async Task Restore_does_not_bring_back_a_milestone_deleted_from_a_live_event()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        var milestone = await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);
        var backup = await BackupAsync(a);
        await a.Occasions.RemoveMilestoneAsync(milestone.Id);

        await RestoreAsync(a, backup);
        await SyncedAsync(a, cloud);
        await SyncedAsync(b, cloud);

        Assert.Empty((await a.Occasions.GetAllAsync()).Single().Milestones);
        Assert.Empty((await b.Occasions.GetAllAsync()).Single().Milestones);
    }

    [Fact]
    public async Task Restore_keeps_the_live_pin_and_adds_the_backup_event_unpinned()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var live = await a.AddOccasionAsync("Live");
        var q = await a.AddOccasionAsync("Q");
        await a.Occasions.SetPinnedAsync(q.Id);
        await SyncedAsync(a, cloud);
        var backup = await BackupAsync(a);
        backup.Occasions.Add(new OccasionDto
        {
            SyncId = Guid.NewGuid(), Title = "Also pinned", AnchorDay = new DateOnly(2024, 1, 1), Direction = "Since",
            IsPinned = true, UpdatedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        });
        await a.Occasions.DeleteAsync(q.Id);
        await a.Occasions.SetPinnedAsync(live.Id);

        var outcome = await RestoreAsync(a, backup);

        Assert.Equal(new ImportOutcome(true, 1, 1, 1), outcome);
        var all = await a.Occasions.GetAllAsync();
        Assert.Equal(new[] { "Also pinned", "Live", "Q" }, all.Select(o => o.Title).OrderBy(t => t));
        Assert.Equal("Live", Assert.Single(all, o => o.IsPinned).Title);
        Assert.Equal(1, await a.Db.Occasions.IgnoreQueryFilters().CountAsync(o => o.IsPinned && o.DeletedAt == null, TestContext.Current.CancellationToken));
        await SyncedAsync(a, cloud);
    }

    [Fact]
    public async Task Legacy_restore_skips_events_that_still_match()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var family = await a.Categories.CreateAsync(new Markd.Core.Domain.Category { Name = "Family", Emoji = "🏠", ColorHex = "#FF0000" });
        var x = await a.AddOccasionOnAsync("Wedding", new DateOnly(2020, 6, 1));
        x.CategoryId = family.Id;
        await a.Occasions.UpdateAsync(x);
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.AddOccasionOnAsync("Trip renamed", new DateOnly(2021, 3, 1));
        await SyncedAsync(a, cloud);
        var v1 = new ExportModel
        {
            SchemaVersion = "1",
            Categories = { new CategoryDto { SourceId = 1, Name = " family", Emoji = "🏠", ColorHex = "#FF0000" } },
            Occasions =
            {
                new OccasionDto { SourceId = 1, Title = "WEDDING", AnchorDate = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc), Direction = "Since", CategorySourceId = 1 },
                new OccasionDto { SourceId = 2, Title = "Trip", AnchorDate = new DateTime(2021, 3, 1, 0, 0, 0, DateTimeKind.Utc), Direction = "Since" },
                new OccasionDto { SourceId = 3, Title = "Graduation", AnchorDate = new DateTime(2019, 7, 1, 0, 0, 0, DateTimeKind.Utc), Direction = "Since", CategorySourceId = 1 },
            },
            Milestones = { new MilestoneDto { SourceId = 1, OccasionSourceId = 1, ThresholdDays = 100, Label = "100 days " } },
        };
        Assert.True(ImportService.IsLegacy(v1));

        var outcome = await RestoreAsync(a, v1);

        Assert.Equal(new ImportOutcome(true, 2, 0, 1), outcome);
        Assert.Equal(new[] { "Graduation", "Trip", "Trip renamed", "Wedding" }, await a.TitlesAsync());   // the renamed one appears twice
        Assert.Equal(family.Id, Assert.Single(await a.Categories.GetAllAsync()).Id);
        var all = await a.Occasions.GetAllAsync();
        Assert.Equal(family.Id, all.Single(o => o.Title == "Graduation").CategoryId);
        Assert.Single(all.Single(o => o.Title == "Wedding").Milestones);
        await SyncedAsync(a, cloud);
    }

    [Fact]
    public async Task Restore_without_sync_state_still_replaces()
    {
        using var a = new SyncDevice();
        await a.AddOccasionAsync("X");
        var backup = await BackupAsync(a);
        var y = await a.AddOccasionAsync("Y");
        Assert.False(await a.State.HasSyncStateAsync());

        var outcome = await RestoreAsync(a, backup);

        Assert.False(outcome.Additive);
        Assert.Equal(new[] { "X" }, await a.TitlesAsync());
        var tomb = await a.Db.Occasions.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.SyncId == y.SyncId, TestContext.Current.CancellationToken);
        Assert.NotNull(tomb.DeletedAt);
    }

    [Fact]
    public async Task A_disconnected_device_with_a_kept_base_has_sync_state()
    {
        using var a = new SyncDevice();
        Assert.False(await a.State.HasSyncStateAsync());
        await a.State.SaveAsync(new Markd.Core.Domain.SyncState { ProviderId = "test", IsConnected = false, BaseSnapshotJson = "{}" });
        Assert.True(await a.State.HasSyncStateAsync());
    }

    [Fact]
    public void Only_schema_1_is_legacy()
    {
        Assert.True(ImportService.IsLegacy(new ExportModel { SchemaVersion = "1" }));
        Assert.False(ImportService.IsLegacy(new ExportModel()));
    }
}
