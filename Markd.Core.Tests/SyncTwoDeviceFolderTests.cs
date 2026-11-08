using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// The whole two-device story over the developer folder provider, the automated stand-in for running two app
/// instances on one PC: connect, edit, conflict, erase and reconnect.
/// </summary>
public sealed class SyncTwoDeviceFolderTests : IDisposable
{
    private const string Passphrase = "correct horse";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "markd-twodevice-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public async Task Two_devices_connect_edit_resolve_a_conflict_and_survive_an_erase_through_a_folder()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        a.Secrets.Passphrase = b.Secrets.Passphrase = null;   // neither device is connected yet
        var cloudA = new LocalFolderCloudProvider(_folder);
        var cloudB = new LocalFolderCloudProvider(_folder);

        // Connect A: no cloud file yet, so A chooses the passphrase and creates the file.
        var x = await a.AddOccasionAsync("Wedding");
        await a.AddOccasionAsync("Move");
        Assert.Null(await cloudA.GetInfoAsync(default));
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.UnlockAsync(cloudA, Passphrase)).Outcome);
        Assert.True((await a.Sync.SyncAsync(cloudA)).IsSuccess);
        Assert.NotNull(await cloudA.GetInfoAsync(default));

        // Connect B: a wrong passphrase stores nothing, the right one unlocks and the first sync brings A's events.
        Assert.Equal(SyncOutcome.WrongPassphrase, (await b.Sync.UnlockAsync(cloudB, "wrong guess")).Outcome);
        Assert.Null(b.Secrets.Passphrase);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.UnlockAsync(cloudB, Passphrase)).Outcome);
        Assert.True((await b.Sync.SyncAsync(cloudB)).IsSuccess);
        Assert.Equal(new[] { "Move", "Wedding" }, await b.TitlesAsync());

        // An edit on A appears on B.
        x.Notes = "Garden party";
        await a.Occasions.UpdateAsync(x);
        Assert.True((await a.Sync.SyncAsync(cloudA)).IsSuccess);
        Assert.True((await b.Sync.SyncAsync(cloudB)).IsSuccess);
        Assert.Equal("Garden party", (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Wedding").Notes);

        // Conflicting edits: B is asked, takes the other device's version, and both converge.
        var onA = (await a.Occasions.GetAllAsync()).Single(o => o.Title == "Move");
        onA.Notes = "From A";
        await a.Occasions.UpdateAsync(onA);
        var onB = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Move");
        onB.Notes = "From B";
        await b.Occasions.UpdateAsync(onB);
        Assert.True((await a.Sync.SyncAsync(cloudA)).IsSuccess);

        var pending = await b.Sync.SyncAsync(cloudB);
        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        Assert.IsType<OccasionConflict>(Assert.Single(pending.Conflicts!));
        Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloudB, Snap.Pick(pending.Conflicts!, ConflictChoice.Remote))).Outcome);
        Assert.True((await a.Sync.SyncAsync(cloudA)).IsSuccess);
        Assert.Equal("From A", (await a.Occasions.GetAllAsync()).Single(o => o.Title == "Move").Notes);
        Assert.Equal("From A", (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Move").Notes);

        // Erase A (forget the link, purge without tombstones): A is empty and unlinked, the cloud and B keep everything.
        Assert.Equal(SyncOutcome.Disconnected, (await a.Sync.ForgetLinkAsync()).Outcome);
        await a.Occasions.PurgeAllAsync();
        Assert.Empty(await a.TitlesAsync());
        Assert.False(await a.State.HasSyncStateAsync());
        Assert.True((await b.Sync.SyncAsync(cloudB)).IsSuccess);
        Assert.Equal(new[] { "Move", "Wedding" }, await b.TitlesAsync());

        // Reconnect A: everything comes back without conflicts, and B is unchanged.
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.UnlockAsync(cloudA, Passphrase)).Outcome);
        var back = await a.Sync.SyncAsync(cloudA);
        Assert.Equal(SyncOutcome.Synced, back.Outcome);
        Assert.Null(back.Conflicts);
        Assert.Equal(new[] { "Move", "Wedding" }, await a.TitlesAsync());
        Assert.Equal("Garden party", (await a.Occasions.GetAllAsync()).Single(o => o.Title == "Wedding").Notes);
        Assert.Equal("From A", (await a.Occasions.GetAllAsync()).Single(o => o.Title == "Move").Notes);
        Assert.True((await b.Sync.SyncAsync(cloudB)).IsSuccess);
        Assert.Equal(new[] { "Move", "Wedding" }, await b.TitlesAsync());
        Assert.Equal("From A", (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Move").Notes);
    }
}
