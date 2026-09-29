using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// A device that disconnects and later reconnects to the same file has no base, but the records it shares with the
/// file are not new to either side: a delete on one side against the live record on the other must be asked about.
/// </summary>
public class SyncReconnectTests
{
    private static async Task<(SyncDevice a, SyncDevice b, InMemoryCloudProvider cloud, int xId)> PairedAsync()
    {
        var a = new SyncDevice();
        var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        return (a, b, cloud, x.Id);
    }

    private static async Task AssertConvergedAsync(SyncDevice a, SyncDevice b, InMemoryCloudProvider cloud, string[] titles)
    {
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(titles, await a.TitlesAsync());
        Assert.Equal(titles, await b.TitlesAsync());
    }

    [Theory]
    [InlineData(ConflictChoice.Local, new[] { "Y" })]
    [InlineData(ConflictChoice.Remote, new[] { "X", "Y" })]
    public async Task A_delete_made_while_disconnected_is_asked_about_on_reconnect(ConflictChoice choice, string[] expected)
    {
        var (a, b, cloud, xId) = await PairedAsync();
        using var _ = a;
        using var __ = b;
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        await a.Occasions.DeleteAsync(xId);
        a.Secrets.Passphrase = "pass";
        var uploads = cloud.Uploads;

        var pending = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var conflict = Assert.Single(pending.Conflicts!);
        Assert.Null(conflict.Local);
        Assert.NotNull(conflict.Remote);
        Assert.Equal(new[] { "Y" }, await a.TitlesAsync());   // nothing changed while undecided
        Assert.Equal(uploads, cloud.Uploads);

        var done = await a.Sync.SyncAsync(cloud, new Dictionary<Guid, ConflictChoice> { [conflict.SyncId] = choice });
        Assert.Equal(SyncOutcome.Synced, done.Outcome);
        Assert.Equal(expected, await a.TitlesAsync());
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);
        await AssertConvergedAsync(a, b, cloud, expected);
        if (choice == ConflictChoice.Remote)
            Assert.Single((await a.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }

    [Theory]
    [InlineData(ConflictChoice.Local, new[] { "X", "Y" })]
    [InlineData(ConflictChoice.Remote, new[] { "Y" })]
    public async Task A_delete_made_by_the_other_device_while_disconnected_is_asked_about_on_reconnect(ConflictChoice choice, string[] expected)
    {
        var (a, b, cloud, _) = await PairedAsync();
        using var _a = a;
        using var _b = b;
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        await b.Occasions.DeleteAsync((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Id);
        await b.Sync.SyncAsync(cloud);
        a.Secrets.Passphrase = "pass";
        var uploads = cloud.Uploads;

        var pending = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var conflict = Assert.Single(pending.Conflicts!);
        Assert.NotNull(conflict.Local);
        Assert.Null(conflict.Remote);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());   // nothing changed while undecided
        Assert.Equal(uploads, cloud.Uploads);

        var done = await a.Sync.SyncAsync(cloud, new Dictionary<Guid, ConflictChoice> { [conflict.SyncId] = choice });
        Assert.Equal(SyncOutcome.Synced, done.Outcome);
        Assert.True((await b.Sync.SyncAsync(cloud)).IsSuccess);
        await AssertConvergedAsync(a, b, cloud, expected);
        if (choice == ConflictChoice.Local)
            Assert.Single((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }
}
