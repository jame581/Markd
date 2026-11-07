using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

public class SyncPassphraseChangeTests
{
    [Fact]
    public async Task Changing_the_passphrase_keeps_other_devices_free_of_conflicts()
    {
        using var a = new SyncDevice("old");
        using var b = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        var epoch = SyncSnapshotCodec.Decode(cloud.Data!, "old").SyncEpoch;
        await RenameOnlyAsync(a, "X edited");

        var changed = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.Synced, changed.Outcome);
        Assert.Equal("new", a.Secrets.Passphrase);
        Assert.Equal(SyncOutcome.WrongPassphrase, (await b.Sync.SyncAsync(cloud)).Outcome);
        b.Secrets.Passphrase = "new";
        var result = await b.Sync.SyncAsync(cloud);
        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Null(result.Conflicts);
        Assert.Equal(new[] { "X edited" }, await b.TitlesAsync());
        Assert.Equal(epoch, SyncSnapshotCodec.Decode(cloud.Data!, "new").SyncEpoch);
        Assert.Equal(epoch, (await a.State.LoadAsync()).Epoch);
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task Changing_the_passphrase_carries_this_devices_pending_edits()
    {
        using var a = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await RenameOnlyAsync(a, "X edited");

        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        var remote = SyncSnapshotCodec.Decode(cloud.Data!, "new");
        Assert.Equal("X edited", Assert.Single(remote.Occasions).Title);
        var state = await a.State.LoadAsync();
        Assert.Equal(cloud.Revision, state.RemoteRevision);
        Assert.Equal(nameof(SyncOutcome.Synced), state.LastAttemptResult);
    }

    [Fact]
    public async Task A_passphrase_change_never_overwrites_an_edit_uploaded_in_between()
    {
        using var a = new SyncDevice("old");
        using var b = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.AddOccasionAsync("Y");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        var uploads = cloud.Uploads;

        // a has nothing pending, so its first upload is the re-encrypted file; b gets in first
        cloud.BeforeUpload = () => Task.Run(async () =>
        {
            var y = (await b.Occasions.GetAllAsync()).Single(o => o.Title == "Y");
            y.Title = "Y from B";
            await b.Occasions.UpdateAsync(y);
            Assert.Equal(SyncOutcome.Synced, (await b.Sync.SyncAsync(cloud)).Outcome);
        }).GetAwaiter().GetResult();
        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(uploads + 2, cloud.Uploads);   // b's edit, then a's re-encryption after a retry
        var remote = SyncSnapshotCodec.Decode(cloud.Data!, "new");
        Assert.Equal(new[] { "X", "Y from B" }, remote.Occasions.Select(o => o.Title).OrderBy(t => t).ToArray());
        Assert.Equal(new[] { "X", "Y from B" }, await a.TitlesAsync());
        Assert.Equal("new", a.Secrets.Passphrase);
    }

    [Fact]
    public async Task A_failed_passphrase_change_keeps_the_old_passphrase()
    {
        using var a = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        cloud.Offline = true;

        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        Assert.Equal("old", a.Secrets.Passphrase);
        cloud.Offline = false;
        Assert.Equal("X", Assert.Single(SyncSnapshotCodec.Decode(cloud.Data!, "old").Occasions).Title);
        Assert.Equal(nameof(SyncOutcome.Offline), (await a.State.LoadAsync()).LastAttemptResult);
    }

    [Fact]
    public async Task A_new_passphrase_the_device_could_not_store_is_reported_with_a_stable_marker()
    {
        using var a = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        a.Secrets.FailOnSet = true;

        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(SyncResult.PassphraseNotStoredDetail, result.Detail);
        Assert.Equal("X", Assert.Single(SyncSnapshotCodec.Decode(cloud.Data!, "new").Occasions).Title);   // the cloud already moved
        Assert.Equal("old", a.Secrets.Passphrase);
        a.Secrets.FailOnSet = false;
        Assert.Equal(SyncOutcome.WrongPassphrase, (await a.Sync.SyncAsync(cloud)).Outcome);   // asks for it again, never re-uploads under the old one
    }

    [Fact]
    public async Task A_passphrase_change_on_a_disconnected_device_asks_for_the_passphrase_and_uploads_nothing()
    {
        using var a = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await a.Sync.DisconnectAsync(cloud, deleteRemote: false);
        var uploads = cloud.Uploads;

        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.NeedsPassphrase, result.Outcome);
        Assert.Equal(uploads, cloud.Uploads);
        Assert.Null(a.Secrets.Passphrase);
        Assert.Equal("X", Assert.Single(SyncSnapshotCodec.Decode(cloud.Data!, "old").Occasions).Title);
    }

    [Fact]
    public async Task A_passphrase_change_with_a_conflict_returns_the_conflict()
    {
        using var a = new SyncDevice("old");
        using var b = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await RenameOnlyAsync(a, "X from A");
        await RenameOnlyAsync(b, "X from B");
        await b.Sync.SyncAsync(cloud);

        var result = await a.Sync.ChangePassphraseAsync(cloud, "new");

        Assert.Equal(SyncOutcome.NeedsConflictResolution, result.Outcome);
        Assert.Single(result.Conflicts!);
        Assert.Equal("old", a.Secrets.Passphrase);
        Assert.Equal("X from B", Assert.Single(SyncSnapshotCodec.Decode(cloud.Data!, "old").Occasions).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_new_passphrase_changes_nothing(string passphrase)
    {
        using var a = new SyncDevice("old");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        var uploads = cloud.Uploads;

        var result = await a.Sync.ChangePassphraseAsync(cloud, passphrase);

        Assert.Equal(SyncOutcome.NeedsPassphrase, result.Outcome);
        Assert.Equal("old", a.Secrets.Passphrase);
        Assert.Equal(uploads, cloud.Uploads);
    }

    private static async Task RenameOnlyAsync(SyncDevice device, string title)
    {
        var occasion = (await device.Occasions.GetAllAsync()).Single();
        occasion.Title = title;
        await device.Occasions.UpdateAsync(occasion);
    }
}
