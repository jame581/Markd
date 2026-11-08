using Markd.Core.Domain;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// Erasing a synced device forgets the link first, then purges local data without leaving tombstones, so the cloud
/// keeps everything and a later reconnect brings it back instead of deleting it everywhere.
/// </summary>
public class SyncEraseTests
{
    private static async Task EraseAsync(SyncDevice device)
    {
        Assert.Equal(SyncOutcome.Disconnected, (await device.Sync.ForgetLinkAsync()).Outcome);
        await device.Occasions.PurgeAllAsync();
    }

    [Fact]
    public async Task Erasing_a_synced_device_then_reconnecting_brings_everything_back()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await EraseAsync(a);

        Assert.Empty(await a.TitlesAsync());
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);
        Assert.Equal(new[] { "X", "Y" }, await b.TitlesAsync());

        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.UnlockAsync(cloud, "pass")).Outcome);
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Null(result.Conflicts);
        Assert.True(result.LocalDataChanged);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());
        Assert.Single((await a.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);
        Assert.Equal(new[] { "X", "Y" }, await b.TitlesAsync());
        Assert.Single((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }

    [Fact]
    public async Task Erasing_then_reconnecting_while_a_category_was_renamed_does_not_conflict()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var category = await a.Categories.CreateAsync(new Category { Name = "Family" });
        var x = await a.AddOccasionAsync("X");
        x.CategoryId = category.Id;
        await a.Occasions.UpdateAsync(x);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        await EraseAsync(a);
        var onB = Assert.Single(await b.Categories.GetAllAsync());
        onB.Name = "Kin";
        await b.Categories.UpdateAsync(onB);
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);

        await a.Sync.UnlockAsync(cloud, "pass");
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Null(result.Conflicts);
        var kept = Assert.Single(await a.Categories.GetAllAsync());
        Assert.Equal("Kin", kept.Name);
        Assert.Equal(kept.Id, Assert.Single(await a.Occasions.GetAllAsync()).CategoryId);
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);
        Assert.Equal("Kin", Assert.Single(await b.Categories.GetAllAsync()).Name);
    }

    [Fact]
    public async Task Forget_link_leaves_the_cloud_file_and_clears_state_and_passphrase()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        var data = cloud.Data;
        var revision = cloud.Revision;
        var calls = (cloud.GetInfoCalls, cloud.Downloads, cloud.Uploads);

        var result = await a.Sync.ForgetLinkAsync();

        Assert.Equal(SyncOutcome.Disconnected, result.Outcome);
        Assert.Same(data, cloud.Data);
        Assert.Equal(revision, cloud.Revision);
        Assert.Equal(calls, (cloud.GetInfoCalls, cloud.Downloads, cloud.Uploads));
        Assert.False(await a.State.HasSyncStateAsync());
        Assert.Null((await a.State.LoadAsync()).LastAttemptUtc);   // no attempt recorded: the row is gone
        Assert.Null(a.Secrets.Passphrase);
        Assert.Equal(new[] { "X" }, await a.TitlesAsync());   // purging local data is up to the caller
    }

    [Fact]
    public async Task Purge_removes_tombstones_too()
    {
        using var a = new SyncDevice();
        var x = await a.AddOccasionAsync("X");
        var milestone = await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.Occasions.AddMilestoneAsync(x.Id, 200, "200 days");
        await a.Occasions.RemoveMilestoneAsync(milestone.Id);   // a milestone tombstone on a live occasion
        var y = await a.AddOccasionAsync("Y");
        await a.Occasions.AddMilestoneAsync(y.Id, 100, "100 days");
        await a.Occasions.DeleteAsync(y.Id);   // an occasion tombstone with its milestone
        await a.Categories.CreateAsync(new Category { Name = "Kept" });
        var gone = await a.Categories.CreateAsync(new Category { Name = "Gone" });
        await a.Categories.DeleteAsync(gone.Id);   // a category tombstone

        await a.Occasions.PurgeAllAsync();

        Assert.Equal(0, await a.Db.Occasions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await a.Db.Milestones.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await a.Db.Categories.IgnoreQueryFilters().CountAsync());
        Assert.Empty(a.Db.ChangeTracker.Entries<Occasion>());
        Assert.Empty(a.Db.ChangeTracker.Entries<Milestone>());
        Assert.Empty(a.Db.ChangeTracker.Entries<Category>());
        var left = await a.Snapshots.ReadForSyncAsync();
        Assert.Empty(left.Occasions);
        Assert.Empty(left.Milestones);
        Assert.Empty(left.Categories);

        await a.AddOccasionAsync("Z");   // the context keeps working after the purge
        Assert.Equal(new[] { "Z" }, await a.TitlesAsync());
    }
}
