using Markd.Core.Domain;
using Markd.Core.Services;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SyncServiceTests
{
    [Fact]
    public async Task First_sync_of_a_device_with_data_uploads_an_encrypted_snapshot()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Wedding");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.NotNull(cloud.Data);
        Assert.True(MarkdPackage.IsEncrypted(cloud.Data!));
        Assert.DoesNotContain("Wedding", System.Text.Encoding.UTF8.GetString(cloud.Data!));
        var state = await a.State.LoadAsync();
        Assert.Equal("test", state.ProviderId);
        Assert.Equal(cloud.Revision, state.RemoteRevision);
        Assert.NotNull(state.BaseSnapshotJson);
    }

    [Fact]
    public async Task A_second_empty_device_downloads_everything()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var wedding = await a.AddOccasionAsync("Wedding");
        await a.Occasions.AddMilestoneAsync(wedding.Id, 100, "100 days");
        await a.Sync.SyncAsync(cloud);

        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Wedding" }, await b.TitlesAsync());
        Assert.Single((await b.Occasions.GetAllAsync())[0].Milestones);
    }

    [Fact]
    public async Task Syncing_again_with_nothing_changed_does_no_work()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(cloud);
        var uploads = cloud.Uploads;
        var downloads = cloud.Downloads;

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NoChanges, result.Outcome);
        Assert.Equal(uploads, cloud.Uploads);
        Assert.Equal(downloads, cloud.Downloads);
    }

    [Fact]
    public async Task An_edit_on_one_device_reaches_the_other_without_conflict()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Trip");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var edit = (await a.Occasions.GetByIdAsync(o.Id))!;
        edit.Title = "Trip to Rome";
        await a.Occasions.UpdateAsync(edit);
        await a.Sync.SyncAsync(cloud);
        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Trip to Rome" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Edits_to_different_records_on_both_devices_merge_silently()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("One");
        await a.AddOccasionAsync("Two");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var aOne = (await a.Occasions.GetAllAsync()).Single(o => o.Title == "One");
        aOne.Title = "One (a)";
        await a.Occasions.UpdateAsync(aOne);
        var bTwo = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Two");
        bTwo.Title = "Two (b)";
        await b.Occasions.UpdateAsync(bTwo);

        await a.Sync.SyncAsync(cloud);
        var second = await b.Sync.SyncAsync(cloud);
        var third = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, second.Outcome);
        Assert.Equal(new[] { "One (a)", "Two (b)" }, await b.TitlesAsync());
        Assert.Equal(new[] { "One (a)", "Two (b)" }, await a.TitlesAsync());
        Assert.True(third.IsSuccess);
    }

    [Fact]
    public async Task A_delete_propagates()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Doomed");
        await a.AddOccasionAsync("Stays");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await a.Occasions.DeleteAsync(o.Id);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        Assert.Equal(new[] { "Stays" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Both_devices_holding_the_same_events_do_not_duplicate_them_or_reset_Notified()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var ao = await a.AddOccasionAsync("Wedding", daysAgo: 200);
        await a.Occasions.AddMilestoneAsync(ao.Id, 100, "100 days");
        var bo = await b.AddOccasionAsync("Wedding", daysAgo: 200);
        var bm = await b.Occasions.AddMilestoneAsync(bo.Id, 100, "100 days");
        await b.Occasions.MarkMilestoneNotifiedAsync(bm.Id);
        await a.Sync.SyncAsync(cloud);

        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        var all = await b.Occasions.GetAllAsync();
        var only = Assert.Single(all);
        Assert.Equal(bo.Id, only.Id);                  // renamed in place, not recreated
        Assert.True(Assert.Single(only.Milestones).Notified);
        await a.Sync.SyncAsync(cloud);
        Assert.Single(await a.Occasions.GetAllAsync());
    }

    [Fact]
    public async Task Syncing_does_not_touch_local_Notified_or_settings()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("x");
        var m = await a.Occasions.AddMilestoneAsync(o.Id, 1000, "far away");   // not reached yet, so b must not treat it as announced
        await a.Occasions.MarkMilestoneNotifiedAsync(m.Id);
        var themeBefore = (await b.Db.AppSettings.AsNoTracking().SingleAsync()).Theme;
        await b.Db.Database.ExecuteSqlRawAsync("UPDATE AppSettings SET Theme = 'Dark'");

        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        Assert.False((await b.Occasions.GetAllAsync())[0].Milestones.Single().Notified);
        Assert.Equal("Dark", (await b.Db.AppSettings.AsNoTracking().SingleAsync()).Theme);
        Assert.NotEqual("Dark", themeBefore);
    }

    [Fact]
    public async Task A_milestone_that_arrives_already_reached_does_not_fire_again_on_the_new_device()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Wedding", daysAgo: 200);
        await a.Occasions.AddMilestoneAsync(o.Id, 100, "100 days");
        await a.Sync.SyncAsync(cloud);

        await b.Sync.SyncAsync(cloud);

        Assert.Empty(await b.Occasions.GetPendingMilestonesAsync());
    }

    [Fact]
    public async Task An_occasion_typed_while_a_sync_is_running_is_not_lost()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await b.AddOccasionAsync("From B");
        await b.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("Old on A");

        // the user adds one on A after the sync has read local data but before it applies the merge
        cloud.OnDownload = () => Task.Run(() => a.AddOccasionAsync("Typed meanwhile")).GetAwaiter().GetResult();
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "From B", "Old on A", "Typed meanwhile" }, await a.TitlesAsync());
        await b.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "From B", "Old on A", "Typed meanwhile" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Concurrent_sync_calls_are_serialized_and_produce_no_duplicates()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");

        var results = await Task.WhenAll(a.Sync.SyncAsync(cloud), a.Sync.SyncAsync(cloud), a.Sync.SyncAsync(cloud));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(1, cloud.Uploads);
        Assert.Single(await a.Occasions.GetAllAsync());
    }
}
