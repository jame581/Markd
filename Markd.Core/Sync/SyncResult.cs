namespace Markd.Core.Sync
{
    public enum SyncOutcome
    {
        Synced, NoChanges, NeedsConflictResolution, NeedsPassphrase, WrongPassphrase,
        AuthExpired, Offline, RemoteTooNew, Failed
    }

    /// <summary>What a sync run did. SyncService never throws into the UI: every failure is an outcome.</summary>
    public sealed record SyncResult(SyncOutcome Outcome, IReadOnlyList<SyncConflict>? Conflicts = null, string? Detail = null)
    {
        public bool IsSuccess => Outcome is SyncOutcome.Synced or SyncOutcome.NoChanges;
    }
}
