using Markd.Core.Domain;
using Markd.Core.Sync;
using Xunit;

namespace Markd.Core.Tests;

public class SyncStateStoreTests
{
    [Fact]
    public async Task Load_returns_an_empty_state_first_then_what_was_saved_then_empty_after_clear()
    {
        var (db, conn) = TestDbFactory.CreateSqliteInMemoryContext();
        using var _ = conn; using var __ = db;
        var store = new SyncStateStore(db);
        var epoch = Guid.NewGuid();

        var empty = await store.LoadAsync();
        Assert.Null(empty.ProviderId);
        Assert.Null(empty.BaseSnapshotJson);

        await store.SaveAsync(new SyncState { ProviderId = "onedrive", AccountName = "a@b", RemoteRevision = "7", LastSyncUtc = Snap.T0, LastResult = "Synced", BaseSnapshotJson = "{}", Epoch = epoch });
        var loaded = await store.LoadAsync();
        Assert.Equal(epoch, loaded.Epoch);
        Assert.Equal("onedrive", loaded.ProviderId);
        Assert.Equal("7", loaded.RemoteRevision);
        Assert.Equal("{}", loaded.BaseSnapshotJson);

        await store.SaveAsync(new SyncState { ProviderId = "onedrive", RemoteRevision = "8" });
        Assert.Equal("8", (await store.LoadAsync()).RemoteRevision);

        await store.ClearAsync();
        Assert.Null((await store.LoadAsync()).ProviderId);
    }

    [Fact]
    public void SyncResult_IsSuccess_covers_Synced_and_NoChanges_only()
    {
        Assert.True(new SyncResult(SyncOutcome.Synced).IsSuccess);
        Assert.True(new SyncResult(SyncOutcome.NoChanges).IsSuccess);
        Assert.False(new SyncResult(SyncOutcome.Offline).IsSuccess);
        Assert.False(new SyncResult(SyncOutcome.NeedsConflictResolution).IsSuccess);
    }
}
