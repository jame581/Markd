using Markd.Core.Domain;
using Markd.Core.Services;
using Markd.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Markd.Core.Tests;

public class SyncServiceFailureTests
{
    [Fact]
    public async Task Without_a_passphrase_nothing_happens()
    {
        using var a = new SyncDevice();
        a.Secrets.Passphrase = null;
        var cloud = new InMemoryCloudProvider();

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NeedsPassphrase, result.Outcome);
        Assert.Equal(0, cloud.Uploads);
    }

    [Fact]
    public async Task A_wrong_passphrase_is_reported_and_local_data_is_untouched()
    {
        using var a = new SyncDevice("right");
        using var c = new SyncDevice("wrong");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Secret");
        await a.Sync.SyncAsync(cloud);
        await c.AddOccasionAsync("Mine");

        var result = await c.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.WrongPassphrase, result.Outcome);
        Assert.Equal(new[] { "Mine" }, await c.TitlesAsync());
        Assert.Null((await c.State.LoadAsync()).BaseSnapshotJson);
    }

    [Fact]
    public async Task Offline_and_expired_sign_in_are_outcomes_not_exceptions()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider { Offline = true };
        Assert.Equal(SyncOutcome.Offline, (await a.Sync.SyncAsync(cloud)).Outcome);

        cloud.Offline = false;
        cloud.AuthExpired = true;
        Assert.Equal(SyncOutcome.AuthExpired, (await a.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task A_snapshot_from_a_newer_app_is_never_merged_or_overwritten()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var future = SyncMerger.Empty();
        future.SchemaVersion = "3";
        var bytes = SyncSnapshotCodec.Encode(future, "pass", MarkdPackage.MinIterations);
        cloud.Put(bytes);
        await a.AddOccasionAsync("local");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.RemoteTooNew, result.Outcome);
        Assert.Equal(bytes, cloud.Data);
        Assert.Equal(0, cloud.Uploads);
    }

    [Fact]
    public async Task A_corrupt_remote_file_is_a_failure_and_is_not_overwritten()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        cloud.Put([1, 2, 3, 4]);
        await a.AddOccasionAsync("local");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, cloud.Data);
    }

    [Fact]
    public async Task Both_sides_editing_the_same_record_asks_for_a_decision_and_then_finishes()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var o = await a.AddOccasionAsync("Trip");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);

        var ea = (await a.Occasions.GetAllAsync()).Single(); ea.Title = "Trip A"; await a.Occasions.UpdateAsync(ea);
        var eb = (await b.Occasions.GetAllAsync()).Single(); eb.Title = "Trip B"; await b.Occasions.UpdateAsync(eb);
        await a.Sync.SyncAsync(cloud);

        var pending = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NeedsConflictResolution, pending.Outcome);
        var conflict = Assert.Single(pending.Conflicts!);
        Assert.Equal(new[] { "Trip B" }, await b.TitlesAsync());   // nothing changed while undecided
        Assert.Equal(1, cloud.Uploads - 1);                        // and nothing was uploaded by b

        var done = await b.Sync.SyncAsync(cloud, new Dictionary<Guid, ConflictChoice> { [conflict.SyncId] = ConflictChoice.Remote });
        Assert.Equal(SyncOutcome.Synced, done.Outcome);
        Assert.Equal(new[] { "Trip A" }, await b.TitlesAsync());
        await a.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "Trip A" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task Losing_the_upload_race_re_merges_and_retries()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Base");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("From A");
        await b.AddOccasionAsync("From B");

        // b is about to upload when a sneaks in first
        cloud.BeforeUpload = () => Task.Run(() => a.Sync.SyncAsync(cloud)).GetAwaiter().GetResult();
        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Base", "From A", "From B" }, await b.TitlesAsync());
        await a.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "Base", "From A", "From B" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task Giving_up_after_repeated_races_is_a_failure_not_a_hang()
    {
        using var a = new SyncDevice();
        await a.AddOccasionAsync("x");
        var attempts = 0;
        var racing = new RacingProvider(new InMemoryCloudProvider(), () => attempts++);

        var result = await a.Sync.SyncAsync(racing);

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task A_deleted_cloud_file_is_re_uploaded_and_never_wipes_local_data()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Keep me");
        await a.Sync.SyncAsync(cloud);
        await cloud.DeleteAsync(default);

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Keep me" }, await a.TitlesAsync());
        Assert.NotNull(cloud.Data);
        using var b = new SyncDevice();
        await b.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "Keep me" }, await b.TitlesAsync());
    }

    [Fact]
    public async Task Switching_provider_ignores_the_previous_providers_base()
    {
        using var a = new SyncDevice();
        var first = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(first);

        var second = new OtherProvider(new InMemoryCloudProvider());
        var result = await a.Sync.SyncAsync(second);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.NotNull(second.Inner.Data);
        Assert.Equal("other", (await a.State.LoadAsync()).ProviderId);
    }

    [Fact]
    public async Task ReplaceRemote_overwrites_the_cloud_with_this_device_under_a_new_passphrase()
    {
        using var a = new SyncDevice("old");
        using var forgot = new SyncDevice("whatever");
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Old cloud data");
        await a.Sync.SyncAsync(cloud);
        await forgot.AddOccasionAsync("Fresh");

        var replaced = await forgot.Sync.ReplaceRemoteAsync(cloud, "brand new");

        Assert.Equal(SyncOutcome.Synced, replaced.Outcome);
        Assert.Equal("brand new", forgot.Secrets.Passphrase);
        Assert.Equal(SyncOutcome.NoChanges, (await forgot.Sync.SyncAsync(cloud)).Outcome);

        // the first device is now told its passphrase no longer fits
        Assert.Equal(SyncOutcome.WrongPassphrase, (await a.Sync.SyncAsync(cloud)).Outcome);
        a.Secrets.Passphrase = "brand new";
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);

        // the replaced cloud data must not read as "everything on device a was deleted"
        Assert.Equal(new[] { "Fresh", "Old cloud data" }, await a.TitlesAsync());
        await forgot.Sync.SyncAsync(cloud);
        Assert.Equal(new[] { "Fresh", "Old cloud data" }, await forgot.TitlesAsync());
    }

    [Fact]
    public async Task Disconnect_clears_state_and_passphrase_and_can_delete_the_cloud_file()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(cloud);

        await a.Sync.DisconnectAsync(cloud, deleteRemote: true);

        Assert.Null(cloud.Data);
        Assert.Null(a.Secrets.Passphrase);
        Assert.Null((await a.State.LoadAsync()).ProviderId);
        Assert.Equal(new[] { "x" }, await a.TitlesAsync());
    }

    private sealed class OtherProvider(InMemoryCloudProvider inner) : ICloudSyncProvider
    {
        public InMemoryCloudProvider Inner => inner;
        public string Id => "other";
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => inner.GetInfoAsync(ct);
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => inner.DownloadAsync(ct);
        public Task<string> UploadAsync(byte[] data, string? expected, CancellationToken ct) => inner.UploadAsync(data, expected, ct);
        public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
    }

    /// <summary>Makes every upload fail with a revision conflict, counting the attempts.</summary>
    private sealed class RacingProvider(InMemoryCloudProvider inner, Action onAttempt) : ICloudSyncProvider
    {
        public string Id => "test";
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => inner.GetInfoAsync(ct);
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => inner.DownloadAsync(ct);
        public Task<string> UploadAsync(byte[] data, string? expected, CancellationToken ct)
        {
            onAttempt();
            throw new RevisionConflictException();
        }
        public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
    }
}
