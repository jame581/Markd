namespace Markd.Core.Sync
{
    /// <summary>Syncs local data with one cloud file. Never throws into the UI: every failure is a <see cref="SyncResult"/>.</summary>
    public interface ISyncService
    {
        Task<SyncResult> SyncAsync(
            ICloudSyncProvider provider, IReadOnlyCollection<ConflictResolution>? resolutions = null, CancellationToken ct = default);

        /// <summary>
        /// Overwrites the cloud file with this device's data under a new passphrase and starts a new lineage, so other
        /// devices discard their base. Only for a forgotten passphrase or an unreadable cloud file: to change a known
        /// passphrase use <see cref="ChangePassphraseAsync"/>.
        /// </summary>
        Task<SyncResult> ReplaceRemoteAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default);

        /// <summary>
        /// Re-encrypts the synced cloud data under a new passphrase, keeping its lineage: other devices only ask for the
        /// new passphrase.
        /// </summary>
        /// <remarks>
        /// Two outcomes can leave the cloud on the new passphrase while this device still holds the old one:
        /// <list type="bullet">
        /// <item>The upload succeeded but the new passphrase could not be stored: <see cref="SyncOutcome.Failed"/> with
        /// <see cref="SyncResult.Detail"/> set to <see cref="SyncResult.PassphraseNotStoredDetail"/>. The user has to
        /// enter the new passphrase again.</item>
        /// <item>The upload timed out or the connection dropped after the provider had committed it: reported as
        /// <see cref="SyncOutcome.Offline"/> (or <see cref="SyncOutcome.Failed"/>) although the cloud moved. The next
        /// sync then reports <see cref="SyncOutcome.WrongPassphrase"/>.</item>
        /// </list>
        /// In both cases the sync state is left as it was, so nothing is uploaded under the old passphrase.
        /// </remarks>
        Task<SyncResult> ChangePassphraseAsync(ICloudSyncProvider provider, string newPassphrase, CancellationToken ct = default);

        /// <summary>
        /// Forgets the passphrase and marks the link disconnected, keeping the base so a reconnect to the same file
        /// merges against it. With <paramref name="deleteRemote"/> the cloud file is deleted and the whole sync state
        /// is forgotten. Local data is never touched. Returns <see cref="SyncOutcome.Disconnected"/>.
        /// </summary>
        Task<SyncResult> DisconnectAsync(ICloudSyncProvider? provider, bool deleteRemote, CancellationToken ct = default);

        /// <summary>
        /// Checks a typed passphrase against the cloud file and stores it only when it decodes the file (or there is no
        /// file yet): <see cref="SyncOutcome.NoChanges"/>, or <see cref="SyncOutcome.WrongPassphrase"/> with nothing
        /// stored. Never uploads. Call it before syncing whenever the user types a passphrase for an existing cloud
        /// file, so a typo can never encrypt an upload.
        /// </summary>
        Task<SyncResult> UnlockAsync(ICloudSyncProvider provider, string passphrase, CancellationToken ct = default);

        /// <summary>
        /// Forgets the whole sync state and the passphrase without touching the cloud file or local data. Returns
        /// <see cref="SyncOutcome.Disconnected"/>.
        /// </summary>
        /// <remarks>
        /// Erasing a device that has sync state calls this FIRST and then
        /// <see cref="Services.IOccasionService.PurgeAllAsync"/>, only after this returned
        /// <see cref="SyncOutcome.Disconnected"/>. A crash between the two leaves local data intact and unlinked, never
        /// an empty device that a sync would read as "everything was deleted".
        /// </remarks>
        Task<SyncResult> ForgetLinkAsync(CancellationToken ct = default);
    }
}
