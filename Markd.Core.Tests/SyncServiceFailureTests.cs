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
    public async Task A_failed_attempt_record_is_not_saved_later_by_an_unrelated_change()
    {
        using var a = new SyncDevice();
        await a.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER no_state BEFORE INSERT ON SyncStates BEGIN SELECT RAISE(ABORT, 'disk full'); END");
        var result = await a.Sync.SyncAsync(new InMemoryCloudProvider { Offline = true });
        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        await a.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER no_state");

        await a.AddOccasionAsync("Unrelated");

        Assert.Empty(a.Db.ChangeTracker.Entries<SyncState>());
        Assert.Equal(0, await a.Db.SyncStates.CountAsync());
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

    [Theory]
    [InlineData("version")]
    [InlineData("kdf")]
    [InlineData("iterations")]
    public async Task A_package_format_from_a_newer_app_is_never_merged_or_overwritten(string field)
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var bytes = SyncSnapshotCodecTests.PackageWithPatchedHeader(field);
        cloud.Put(bytes);
        await a.AddOccasionAsync("local");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.RemoteTooNew, result.Outcome);
        Assert.Equal(bytes, cloud.Data);
        Assert.Equal(0, cloud.Uploads);
        Assert.Equal(new[] { "local" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task An_older_package_format_is_reported_as_unreadable_and_is_not_overwritten()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var bytes = SyncSnapshotCodecTests.PackageWithPatchedHeader("version", 1);
        cloud.Put(bytes);
        await a.AddOccasionAsync("local");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.RemoteUnreadable, result.Outcome);
        Assert.Equal(bytes, cloud.Data);
        Assert.Equal(0, cloud.Uploads);
        Assert.Equal(new[] { "local" }, await a.TitlesAsync());
    }

    [Fact]
    public async Task A_corrupt_remote_file_is_reported_as_unreadable_and_is_not_overwritten()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        cloud.Put([1, 2, 3, 4]);
        await a.AddOccasionAsync("local");

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.RemoteUnreadable, result.Outcome);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, cloud.Data);
        Assert.Equal(0, cloud.Uploads);
        Assert.Equal(new[] { "local" }, await a.TitlesAsync());
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

        var done = await b.Sync.SyncAsync(cloud, [conflict.Resolve(ConflictChoice.Remote)]);
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

    private static async Task<(SyncDevice a, SyncDevice b, InMemoryCloudProvider cloud, int tripId)> TripEditedOnAAsync()
    {
        var a = new SyncDevice();
        var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        var trip = await a.AddOccasionAsync("Trip");
        await a.Sync.SyncAsync(cloud);
        await b.Sync.SyncAsync(cloud);
        await RenameAsync(a, trip.Id, "Trip v1");
        await a.Sync.SyncAsync(cloud);
        await b.AddOccasionAsync("Y");
        return (a, b, cloud, trip.Id);
    }

    private static async Task RenameAsync(SyncDevice device, int id, string title)
    {
        var occasion = (await device.Occasions.GetByIdAsync(id))!;
        occasion.Title = title;
        await device.Occasions.UpdateAsync(occasion);
    }

    [Fact]
    public async Task Losing_the_upload_race_after_adopting_a_remote_edit_is_not_a_conflict()
    {
        var (a, b, cloud, tripId) = await TripEditedOnAAsync();
        using var _a = a;
        using var _b = b;

        // b has applied a's "Trip v1" and is about to upload its Y when a edits the trip again and wins the race
        cloud.BeforeUpload = () => Task.Run(async () =>
        {
            await RenameAsync(a, tripId, "Trip v2");
            await a.Sync.SyncAsync(cloud);
        }).GetAwaiter().GetResult();
        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Trip v2", "Y" }, await b.TitlesAsync());
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(new[] { "Trip v2", "Y" }, await a.TitlesAsync());
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task Going_offline_after_adopting_a_remote_edit_is_not_a_conflict_on_the_next_sync()
    {
        var (a, b, cloud, tripId) = await TripEditedOnAAsync();
        using var _a = a;
        using var _b = b;

        // b applies a's "Trip v1" and then loses the network before its upload
        cloud.BeforeUpload = () => throw new SyncOfflineException();
        Assert.Equal(SyncOutcome.Offline, (await b.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(new[] { "Trip v1", "Y" }, await b.TitlesAsync());
        await RenameAsync(a, tripId, "Trip v2");
        await a.Sync.SyncAsync(cloud);

        var result = await b.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "Trip v2", "Y" }, await b.TitlesAsync());
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(new[] { "Trip v2", "Y" }, await a.TitlesAsync());
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
        Assert.Equal(SyncOutcome.NoChanges, (await b.Sync.SyncAsync(cloud)).Outcome);
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
        Assert.Equal("The remote file kept changing.", result.Detail);
    }

    [Fact]
    public async Task Giving_up_after_repeated_local_edits_names_local_changes_as_the_reason()
    {
        using var a = new SyncDevice();
        using var b = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await b.AddOccasionAsync("From B");
        await b.Sync.SyncAsync(cloud);
        await a.AddOccasionAsync("Old on A");
        var edits = 0;
        var editing = new EditingProvider(cloud,
            () => Task.Run(() => a.AddOccasionAsync($"Typed meanwhile {++edits}")).GetAwaiter().GetResult());

        var result = await a.Sync.SyncAsync(editing);

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(3, edits);
        Assert.Equal("Local data kept changing during the sync.", result.Detail);
        Assert.Equal(1, cloud.Uploads);
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
        await a.AddOccasionAsync("X");
        await a.Sync.SyncAsync(first);
        var storedRevision = (await a.State.LoadAsync()).RemoteRevision;

        // the new provider already holds another device's data (without X) under the same revision string
        var second = new OtherProvider(new InMemoryCloudProvider());
        using var other = new SyncDevice();
        await other.AddOccasionAsync("Y");
        await other.Sync.SyncAsync(second);
        Assert.Equal(storedRevision, second.Inner.Revision);   // the collision this test relies on

        var result = await a.Sync.SyncAsync(second);

        Assert.Equal(SyncOutcome.Synced, result.Outcome);
        Assert.Equal(new[] { "X", "Y" }, await a.TitlesAsync());   // X must not read as "deleted remotely"
        Assert.Equal("other", (await a.State.LoadAsync()).ProviderId);
        await other.Sync.SyncAsync(second);
        Assert.Equal(new[] { "X", "Y" }, await other.TitlesAsync());
    }

    [Fact]
    public async Task Switching_account_on_the_same_provider_merges_instead_of_overwriting_the_other_accounts_file()
    {
        using var a = new SyncDevice();
        var personal = new InMemoryCloudProvider { AccountName = "personal@test" };
        await a.AddOccasionAsync("Mine");
        await a.Sync.SyncAsync(personal);

        // the other account on the same provider already holds another device's data under the same revision string
        using var other = new SyncDevice();
        var work = new InMemoryCloudProvider { AccountName = "work@test" };
        await other.AddOccasionAsync("Theirs");
        await other.Sync.SyncAsync(work);
        Assert.Equal(personal.Id, work.Id);
        Assert.Equal(personal.Revision, work.Revision);   // the collision this test relies on

        var switched = await a.Sync.SyncAsync(work);
        await a.AddOccasionAsync("Later");
        var edited = await a.Sync.SyncAsync(work);

        Assert.Equal(SyncOutcome.Synced, switched.Outcome);
        Assert.Equal(SyncOutcome.Synced, edited.Outcome);
        Assert.Equal(new[] { "Later", "Mine", "Theirs" }, await a.TitlesAsync());
        Assert.Contains("Theirs", SyncSnapshotCodec.Decode(work.Data!, "pass").Occasions.Where(o => o.DeletedAt is null).Select(o => o.Title));
        Assert.Equal("work@test", (await a.State.LoadAsync()).AccountName);
        Assert.True((await other.Sync.SyncAsync(work)).IsSuccess);
        Assert.Equal(new[] { "Later", "Mine", "Theirs" }, await other.TitlesAsync());
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

        var result = await a.Sync.DisconnectAsync(cloud, deleteRemote: true);

        Assert.Equal(SyncOutcome.Disconnected, result.Outcome);
        Assert.Null(cloud.Data);
        Assert.Null(a.Secrets.Passphrase);
        Assert.Null((await a.State.LoadAsync()).ProviderId);
        Assert.Equal(new[] { "x" }, await a.TitlesAsync());
    }

    [Theory]
    [InlineData(false, SyncOutcome.Offline)]
    [InlineData(true, SyncOutcome.AuthExpired)]
    public async Task A_failed_remote_delete_on_disconnect_is_an_outcome_and_keeps_the_link(bool authExpired, SyncOutcome expected)
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(cloud);
        var before = await a.State.LoadAsync();
        cloud.Offline = !authExpired;
        cloud.AuthExpired = authExpired;

        var result = await a.Sync.DisconnectAsync(cloud, deleteRemote: true);

        Assert.Equal(expected, result.Outcome);
        Assert.Equal("pass", a.Secrets.Passphrase);
        var after = await a.State.LoadAsync();
        Assert.Equal("test", after.ProviderId);
        Assert.Equal(before.RemoteRevision, after.RemoteRevision);
        Assert.Equal(before.BaseSnapshotJson, after.BaseSnapshotJson);
        cloud.Offline = false;
        cloud.AuthExpired = false;
        Assert.NotNull(cloud.Data);
        Assert.Equal(SyncOutcome.NoChanges, (await a.Sync.SyncAsync(cloud)).Outcome);
    }

    [Fact]
    public async Task The_state_carries_the_providers_account_name_and_drops_it_on_a_provider_switch()
    {
        using var a = new SyncDevice();
        var first = new InMemoryCloudProvider { AccountName = "me@first" };
        await a.AddOccasionAsync("x");

        await a.Sync.SyncAsync(first);
        Assert.Equal("me@first", (await a.State.LoadAsync()).AccountName);

        var second = new OtherProvider(new InMemoryCloudProvider());
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(second)).Outcome);
        Assert.Equal("me@other", (await a.State.LoadAsync()).AccountName);

        // a provider that knows no account name must not inherit the previous provider's
        var unnamed = new InMemoryCloudProvider { AccountName = null };
        Assert.Equal(SyncOutcome.Synced, (await a.Sync.SyncAsync(unnamed)).Outcome);
        Assert.Null((await a.State.LoadAsync()).AccountName);

        Assert.Equal(SyncOutcome.Synced, (await a.Sync.ReplaceRemoteAsync(second, "new")).Outcome);
        Assert.Equal("me@other", (await a.State.LoadAsync()).AccountName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplaceRemote_without_a_passphrase_changes_nothing(string passphrase)
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("x");
        await a.Sync.SyncAsync(cloud);
        var data = cloud.Data;
        var before = await a.State.LoadAsync();

        var result = await a.Sync.ReplaceRemoteAsync(cloud, passphrase);

        Assert.Equal(SyncOutcome.NeedsPassphrase, result.Outcome);
        Assert.Equal(1, cloud.Uploads);
        Assert.Same(data, cloud.Data);
        Assert.Equal("pass", a.Secrets.Passphrase);
        Assert.Equal(before.Epoch, (await a.State.LoadAsync()).Epoch);
    }

    private sealed class OtherProvider(InMemoryCloudProvider inner) : ICloudSyncProvider
    {
        public InMemoryCloudProvider Inner => inner;
        public string Id => "other";
        public string? AccountName => "me@other";
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => inner.GetInfoAsync(ct);
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => inner.DownloadAsync(ct);
        public Task<string> UploadAsync(byte[] data, string? expected, CancellationToken ct) => inner.UploadAsync(data, expected, ct);
        public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
    }

    /// <summary>Simulates the user editing during every download.</summary>
    private sealed class EditingProvider(InMemoryCloudProvider inner, Action onDownload) : ICloudSyncProvider
    {
        public string Id => inner.Id;
        public string? AccountName => inner.AccountName;
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => inner.GetInfoAsync(ct);
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct)
        {
            onDownload();
            return inner.DownloadAsync(ct);
        }
        public Task<string> UploadAsync(byte[] data, string? expected, CancellationToken ct) => inner.UploadAsync(data, expected, ct);
        public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
    }

    /// <summary>Makes every upload fail with a revision conflict, counting the attempts.</summary>
    private sealed class RacingProvider(InMemoryCloudProvider inner, Action onAttempt) : ICloudSyncProvider
    {
        public string Id => "test";
        public string? AccountName => null;
        public Task<RemoteInfo?> GetInfoAsync(CancellationToken ct) => inner.GetInfoAsync(ct);
        public Task<RemoteFile?> DownloadAsync(CancellationToken ct) => inner.DownloadAsync(ct);
        public Task<string> UploadAsync(byte[] data, string? expected, CancellationToken ct)
        {
            onAttempt();
            throw new RevisionConflictException();
        }
        public Task DeleteAsync(CancellationToken ct) => inner.DeleteAsync(ct);
    }

    [Fact]
    public async Task An_offline_sync_records_the_attempt_and_keeps_the_last_success()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Wedding");
        var t1 = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(3);
        a.Clock.Now = t1;
        await a.Sync.SyncAsync(cloud);

        cloud.Offline = true;
        a.Clock.Now = t2;
        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        var state = await a.State.LoadAsync();
        Assert.Equal(t2, state.LastAttemptUtc);
        Assert.Equal("Offline", state.LastAttemptResult);
        Assert.Equal(t1, state.LastSyncUtc);
        Assert.Equal("Synced", state.LastResult);
    }

    [Fact]
    public async Task A_successful_sync_records_the_attempt_too()
    {
        using var a = new SyncDevice();
        var cloud = new InMemoryCloudProvider();
        await a.AddOccasionAsync("Wedding");

        await a.Sync.SyncAsync(cloud);

        var state = await a.State.LoadAsync();
        Assert.Equal("Synced", state.LastAttemptResult);
        Assert.NotNull(state.LastAttemptUtc);
        Assert.Equal(state.LastSyncUtc, state.LastAttemptUtc);
    }

    [Fact]
    public async Task A_missing_passphrase_records_the_attempt_without_linking()
    {
        using var a = new SyncDevice();
        a.Secrets.Passphrase = null;
        var cloud = new InMemoryCloudProvider();

        var result = await a.Sync.SyncAsync(cloud);

        Assert.Equal(SyncOutcome.NeedsPassphrase, result.Outcome);
        var state = await a.State.LoadAsync();
        Assert.Equal("NeedsPassphrase", state.LastAttemptResult);
        Assert.Null(state.ProviderId);
        Assert.False(state.IsConnected);
    }
}
