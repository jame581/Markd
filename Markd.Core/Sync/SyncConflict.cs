using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public enum SyncRecordKind { Category, Occasion, Milestone }

    public enum ConflictChoice { Local, Remote }

    /// <summary>
    /// A record both devices changed differently (or one changed and the other deleted).
    /// Local/Remote on the typed records are null when that side deleted it.
    /// </summary>
    public abstract record SyncConflict(Guid SyncId, SyncRecordKind Kind, DateTime LocalUpdatedAt, DateTime RemoteUpdatedAt)
    {
        public ConflictChoice Newest => RemoteUpdatedAt > LocalUpdatedAt ? ConflictChoice.Remote : ConflictChoice.Local;

        /// <summary>
        /// Identifies the exact local and remote versions this conflict showed (set by the merger). A resolution only
        /// applies while both sides are still those versions.
        /// </summary>
        public string Signature { get; init; } = "";

        public ConflictResolution Resolve(ConflictChoice choice) => new(SyncId, choice, Signature);
    }

    /// <summary>A user's choice for one conflict, bound to the versions that were shown through <see cref="Signature"/>.</summary>
    public sealed record ConflictResolution(Guid SyncId, ConflictChoice Choice, string Signature);

    public sealed record CategoryConflict(Guid SyncId, CategoryDto? Local, CategoryDto? Remote, DateTime LocalUpdatedAt, DateTime RemoteUpdatedAt)
        : SyncConflict(SyncId, SyncRecordKind.Category, LocalUpdatedAt, RemoteUpdatedAt);

    public sealed record OccasionConflict(Guid SyncId, OccasionDto? Local, OccasionDto? Remote,
        string? LocalCategoryName, string? RemoteCategoryName, DateTime LocalUpdatedAt, DateTime RemoteUpdatedAt)
        : SyncConflict(SyncId, SyncRecordKind.Occasion, LocalUpdatedAt, RemoteUpdatedAt);

    public sealed record MilestoneConflict(Guid SyncId, MilestoneDto? Local, MilestoneDto? Remote,
        string? OccasionTitle, DateTime LocalUpdatedAt, DateTime RemoteUpdatedAt)
        : SyncConflict(SyncId, SyncRecordKind.Milestone, LocalUpdatedAt, RemoteUpdatedAt);
}
