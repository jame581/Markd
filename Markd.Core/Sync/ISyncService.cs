namespace Markd.Core.Sync
{
    /// <summary>Syncs local data with one cloud file. Never throws into the UI: every failure is a <see cref="SyncResult"/>.</summary>
    public interface ISyncService
    {
        Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyDictionary<Guid, ConflictChoice>? resolutions = null, CancellationToken ct = default);

        /// <summary>Overwrites the cloud file with this device's data under a new passphrase (forgotten passphrase).</summary>
        Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default);

        /// <summary>
        /// Forgets the passphrase and marks the link disconnected, keeping the base so a reconnect to the same file
        /// merges against it. With <paramref name="deleteRemote"/> the cloud file is deleted and the whole sync state
        /// is forgotten. Local data is never touched. Returns <see cref="SyncOutcome.Disconnected"/>.
        /// </summary>
        Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default);
    }
}
