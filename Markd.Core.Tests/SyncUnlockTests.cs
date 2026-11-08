using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

/// <summary>
/// A typed passphrase is proven against the cloud file before it is stored, so a typo never encrypts an upload.
/// </summary>
public class SyncUnlockTests
{
    [Fact]
    public async Task Unlock_with_a_wrong_passphrase_stores_nothing_and_uploads_nothing()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("Y");   // a local edit waiting to be uploaded
        var uploads = cloud.Uploads;

        var result = await a.Sync.UnlockAsync(cloud, "wrong");

        Assert.Equal(SyncOutcome.WrongPassphrase, result.Outcome);
        Assert.Equal("pass", a.Secrets.Passphrase);
        Assert.Equal(uploads, cloud.Uploads);
    }

    [Fact]
    public async Task Unlock_with_the_right_passphrase_stores_it()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        b.Secrets.Passphrase = null;
        await b.AddOccasionAsync("Y");
        var uploads = cloud.Uploads;

        var result = await b.Sync.UnlockAsync(cloud, "pass");

        Assert.Equal(SyncOutcome.NoChanges, result.Outcome);
        Assert.Equal("pass", b.Secrets.Passphrase);
        Assert.Equal(uploads, cloud.Uploads);
        Assert.Equal(new[] { "Y" }, await b.TitlesAsync());   // unlocking merges nothing
    }

    [Fact]
    public async Task Unlock_with_no_cloud_file_stores_nothing_and_says_so()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        a.Secrets.Passphrase = null;
        await a.AddOccasionAsync("X");

        var result = await a.Sync.UnlockAsync(cloud, "new");

        Assert.Equal(SyncOutcome.NoCloudFile, result.Outcome);
        Assert.Null(a.Secrets.Passphrase);
        Assert.Null(cloud.Data);
        Assert.Equal(0, cloud.Uploads);
    }

    [Fact]
    public async Task Unlock_after_the_cloud_file_was_deleted_elsewhere_keeps_the_stored_passphrase()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await cloud.DeleteAsync(TestContext.Current.CancellationToken);

        var result = await a.Sync.UnlockAsync(cloud, "typo");

        Assert.Equal(SyncOutcome.NoCloudFile, result.Outcome);
        Assert.Equal("pass", a.Secrets.Passphrase);
    }

    [Fact]
    public async Task An_apply_followed_by_an_offline_upload_reports_local_data_changed()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await b.AddOccasionAsync("Z");
        await b.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("Y");   // so A still has something to upload after adopting Z
        cloud.BeforeUpload = () => throw new SyncOfflineException();

        var offline = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Offline, offline.Outcome);
        Assert.True(offline.LocalDataChanged);
        Assert.Equal(new[] { "X", "Y", "Z" }, await a.TitlesAsync());

        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        var quiet = await a.Sync.SyncAsync(cloud);
        Assert.Equal(SyncOutcome.NoChanges, quiet.Outcome);
        Assert.False(quiet.LocalDataChanged);
    }
}
