using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// A disconnect keeps the base, so reconnecting to the same file merges as if the device had never left. A device
/// that reaches a file without a usable base (another account, a replaced file) unions its data in instead, and a
/// delete on one side against the live record on the other must be asked about.
/// </summary>
public class SyncReconnectTests
{
    /// <summary>
    /// A syncs X (with a milestone) and Y to its personal account. B starts on the work account, which holds the
    /// same file under the same revision, so A switching to work later has no base for records it shares with B.
    /// </summary>
    private static async Task<(SyncDevice a, SyncDevice b, InMemoryCloudProvider work, int xId)> SharedAcrossAccountsAsync()
    {
        var a = new SyncDevice();
        var b = new SyncDevice();
        var personal = new InMemoryCloudProvider { AccountName = "personal@test" };
        var work = new InMemoryCloudProvider { AccountName = "work@test" };
        var x = await a.AddOccasionAsync("X");
        await a.Occasions.AddMilestoneAsync(x.Id, 100, "100 days");
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(personal);
        work.CopyFrom(personal);
        await b.Sync.SyncAsync(work);
        return (a, b, work, x.Id);
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
    public async Task A_delete_made_before_switching_account_is_asked_about(ConflictChoice choice, string[] expected)
    {
        var (a, b, work, xId) = await SharedAcrossAccountsAsync();
        using var _ = a;
        using var __ = b;
        await a.Occasions.DeleteAsync(xId);
        var uploads = work.Uploads;

        var pending = await a.Sync.SyncAsync(work);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(pending.Conflicts!));
        Assert.Null(conflict.Local);
        Assert.NotNull(conflict.Remote);
        Assert.Equal(new[] { "Y" }, await a.TitlesAsync());   // nothing changed while undecided
        Assert.Equal(uploads, work.Uploads);

        var done = await a.Sync.SyncAsync(work, [conflict.Resolve(choice)]);
        Assert.Equal(SyncOutcome.Synced, done.Outcome);
        Assert.Equal(expected, await a.TitlesAsync());
        Assert.True((await b.Sync.SyncAsync(work)).IsSuccess);
        await AssertConvergedAsync(a, b, work, expected);
        if (choice == ConflictChoice.Remote)
            Assert.Single((await a.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }

    [Theory]
    [InlineData(ConflictChoice.Local, new[] { "X", "Y" })]
    [InlineData(ConflictChoice.Remote, new[] { "Y" })]
    public async Task A_delete_made_on_the_other_account_is_asked_about_on_switching_to_it(ConflictChoice choice, string[] expected)
    {
        var (a, b, work, _) = await SharedAcrossAccountsAsync();
        using var _a = a;
        using var _b = b;
        await b.Occasions.DeleteAsync((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Id);
        await b.Sync.SyncAsync(work);
        var uploads = work.Uploads;

        var pending = await a.Sync.SyncAsync(work);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var conflict = Assert.IsType<OccasionConflict>(Assert.Single(pending.Conflicts!));
        Assert.NotNull(conflict.Local);
        Assert.Null(conflict.Remote);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());   // nothing changed while undecided
        Assert.Equal(uploads, work.Uploads);

        var done = await a.Sync.SyncAsync(work, [conflict.Resolve(choice)]);
        Assert.Equal(SyncOutcome.Synced, done.Outcome);
        Assert.True((await b.Sync.SyncAsync(work)).IsSuccess);
        await AssertConvergedAsync(a, b, work, expected);
        if (choice == ConflictChoice.Local)
            Assert.Single((await b.Occasions.GetAllAsync()).Single(o => o.Title == "X").Milestones);
    }

    private static async Task<(SyncDevice a, SyncDevice b, InMemoryCloudProvider cloud)> PairedAsync(params string[] titles)
    {
        var a = new SyncDevice();
        var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        foreach (var title in titles)
            await a.AddOccasionAsync(title);
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        return (a, b, cloud);
    }

    private static async Task RenameAsync(SyncDevice device, string from, string to)
    {
        var occasion = (await device.Occasions.GetAllAsync()).Single(o => o.Title == from);
        occasion.Title = to;
        await device.Occasions.UpdateAsync(occasion);
    }

    private static async Task EditThreeOnBAndSyncAsync(SyncDevice b, InMemoryCloudProvider cloud)
    {
        foreach (var title in new[] { "One", "Two", "Three" })
            await RenameAsync(b, title, title + " B");
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task Reconnect_after_disconnect_merges_against_the_kept_base()
    {
        var (a, b, cloud) = await PairedAsync("One", "Two", "Three");
        using var _a = a;
        using var _b = b;
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        await EditThreeOnBAndSyncAsync(b, cloud);
        a.Secrets.Passphrase = "pass";

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Null(result.Conflicts);
        Assert.Equal(new[] { "One B", "Three B", "Two B" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task Reconnect_keeps_edits_made_on_both_sides_while_disconnected()
    {
        var (a, b, cloud) = await PairedAsync("One", "Two", "Three", "Four");
        using var _a = a;
        using var _b = b;
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        await RenameAsync(a, "Four", "Four A");
        await EditThreeOnBAndSyncAsync(b, cloud);
        a.Secrets.Passphrase = "pass";

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        var expected = new[] { "Four A", "One B", "Three B", "Two B" };
        Assert.Equal(expected, await a.TitlesAsync());
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(expected, await b.TitlesAsync());
    }

    [Fact]
    public async Task Reconnect_after_the_file_was_replaced_drops_the_kept_base()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice("whatever");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(cloud);
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        await b.AddOccasionAsync("From B");
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.ReplaceRemoteAsync(cloud, "new")).Outcome);
        a.Secrets.Passphrase = "new";

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "From B", "X", "Y" }, await a.TitlesAsync());   // the replaced file must not read as "X and Y were deleted"
        await b.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "From B", "X", "Y" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Reconnect_to_another_account_drops_the_kept_base()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var first = new InMemoryCloudProvider();
        var second = new InMemoryCloudProvider { AccountName = "other@test" };
        await a.AddOccasionAsync("One");
        await a.Sync.SyncAsync(first);
        await b.Sync.SyncAsync(first);
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(second)).Outcome);   // the second account holds only One
        await a.AddOccasionAsync("Two");
        await a.AddOccasionAsync("Three");
        await a.Sync.SyncAsync(first);
        await a.Sync.DisconnectAsync(first, deleteRemote: false);
        a.Secrets.Passphrase = "pass";

        var result = await a.Sync.SyncAsync(second);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        var all = new[] { "One", "Three", "Two" };
        Assert.Equal(all, await a.TitlesAsync());
        Assert.Equal(all, SyncSnapshotCodec.Decode(second.Data!, "pass").Occasions
            .Where(o => o.DeletedAt is null).Select(o => o.Title).OrderBy(t => t).ToArray());
    }

    [Fact]
    public async Task Disconnect_returns_Disconnected_and_marks_the_state()
    {
        var (a, b, cloud) = await PairedAsync("One");
        using var _a = a;
        using var _b = b;
        Assert.True((await a.State.LoadAsync()).IsConnected);

        var result = await a.Sync.DisconnectAsync(cloud, deleteRemote: false);

        Assert.Equal(SyncOutcome.Disconnected, result.Outcome);
        Assert.True(result.IsSuccess);
        var state = await a.State.LoadAsync();
        Assert.False(state.IsConnected);
        Assert.NotNull(state.BaseSnapshotJson);
        Assert.Equal("test", state.ProviderId);
        Assert.NotNull(state.RemoteRevision);
        Assert.NotNull(state.Epoch);
        Assert.Null(a.Secrets.Passphrase);
        Assert.NotNull(cloud.Data);
    }

    [Fact]
    public async Task Disconnect_with_delete_clears_the_state()
    {
        var (a, b, cloud) = await PairedAsync("One");
        using var _a = a;
        using var _b = b;

        var result = await a.Sync.DisconnectAsync(cloud, deleteRemote: true);

        Assert.Equal(SyncOutcome.Disconnected, result.Outcome);
        Assert.True(result.IsSuccess);
        var state = await a.State.LoadAsync();
        Assert.Null(state.ProviderId);
        Assert.Null(state.BaseSnapshotJson);
        Assert.False(state.IsConnected);
        Assert.Null(a.Secrets.Passphrase);
        Assert.Null(cloud.Data);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reconnect_with_a_wrong_passphrase_never_uploads(bool editWhileDisconnected)
    {
        var (a, b, cloud) = await PairedAsync("One", "Two");
        using var _a = a;
        using var _b = b;
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        if (editWhileDisconnected)
            await RenameAsync(a, "One", "One A");
        a.Secrets.Passphrase = "typo";
        var uploads = cloud.Uploads;
        var data = cloud.Data;

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.WrongPassphrase, result.Outcome);
        Assert.Equal(uploads, cloud.Uploads);
        Assert.Same(data, cloud.Data);
        Assert.Equal(new[] { "One", "Two" }, SyncSnapshotCodec.Decode(cloud.Data!, "pass").Occasions
            .Select(o => o.Title).OrderBy(t => t).ToArray());
        Assert.False((await a.State.LoadAsync()).IsConnected);
    }
}
