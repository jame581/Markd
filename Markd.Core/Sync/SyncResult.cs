namespace Markd.Core.Sync
{
    public enum SyncOutcome
    {
        Synced, NoChanges, NeedsConflictResolution, NeedsPassphrase, WrongPassphrase,
        AuthExpired, Offline, RemoteTooNew, RemoteUnreadable, Failed, Disconnected
    }

    /// <summary>What a sync run did. SyncService never throws into the UI: every failure is an outcome.</summary>
    public sealed record SyncResult(SyncOutcome Outcome, IReadOnlyList<SyncConflict>? Conflicts = null, string? Detail = null)
    {
        /// <summary>
        /// <see cref="Detail"/> of a <see cref="SyncOutcome.Failed"/> passphrase change whose upload succeeded but
        /// whose new passphrase could not be stored on this device: the cloud now uses the new passphrase, so the user
        /// has to enter it again.
        /// </summary>
        public const string PassphraseNotStoredDetail = "passphrase-not-stored";

        /// <summary>
        /// True when the call wrote merged data into the local database, whatever its final outcome: an apply followed
        /// by a failed or offline upload still changed what this device shows.
        /// </summary>
        public bool LocalDataChanged { get; init; }

        public bool IsSuccess => Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges or SyncOutcome.Disconnected;
    }
}
