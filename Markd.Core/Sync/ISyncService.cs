namespace Markd.Core.Sync
{
    /// <summary>Syncs local data with one cloud file. Never throws into the UI: every failure is a <see cref="SyncResult"/>.</summary>
    public interface ISyncService
    {
        Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyDictionary<Guid, ConflictChoice>? resolutions = null, CancellationToken ct = default);

        /// <summary>Overwrites the cloud file with this device's data under a new passphrase (forgotten passphrase).</summary>
        Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default);

        /// <summary>Forgets the link and the passphrase, optionally deleting the cloud file. Local data is never touched.</summary>
        Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default);
    }
}
