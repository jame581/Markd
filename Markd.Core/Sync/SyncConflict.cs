namespace Markd.Core.Sync
{
    public enum SyncRecordKind { Category, Occasion, Milestone }

    public enum ConflictChoice { Local, Remote }

    /// <summary>
    /// A record both devices changed differently (or one changed and the other deleted).
    /// Local/Remote are the DTOs (CategoryDto, OccasionDto, MilestoneDto), or null when that side deleted it.
    /// </summary>
    public sealed record SyncConflict(
        Guid SyncId, SyncRecordKind Kind, object? Local, object? Remote, DateTime LocalUpdatedAt, DateTime RemoteUpdatedAt)
    {
        public ConflictChoice Newest => RemoteUpdatedAt > LocalUpdatedAt ? ConflictChoice.Remote : ConflictChoice.Local;
    }
}
