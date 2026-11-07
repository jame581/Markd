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

        public bool IsSuccess => Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges or SyncOutcome.Disconnected;
    }
}
