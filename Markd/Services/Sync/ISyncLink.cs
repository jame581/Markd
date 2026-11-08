namespace Markd.Services.Sync;

/// <summary>What erase and restore need from the sync coordinator.</summary>
public interface ISyncLink
{
    /// <summary>Whether <see cref="Core.Domain.SyncState.IsConnected"/> was true at the last read.</summary>
    bool IsLinked { get; }

    /// <summary>True when a sync state row with a provider exists: linked, or disconnected with a kept base.</summary>
    Task<bool> HasSyncStateAsync();

    /// <summary>Runs a manual sync; true when it ended synced (nothing left to upload). Never throws.</summary>
    Task<bool> TrySyncNowAsync();

    /// <summary>Forgets the link before an erase. Throws when it did not disconnect.</summary>
    Task ForgetLinkForEraseAsync();
}
