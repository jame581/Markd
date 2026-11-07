using Markd.Core.Services;
using Markd.Core.Sync;

namespace Markd.Core.Tests;

internal static class Snap
{
    public static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Now = T0.AddDays(10);

    public static Guid G(int n) => new(n, 0, 0, new byte[8]);

    public static CategoryDto Cat(int n, string name = "Cat", bool deleted = false, DateTime? updated = null) => new()
    {
        SyncId = G(n), Name = name, UpdatedAt = updated ?? T0, DeletedAt = deleted ? T0 : null
    };

    public static OccasionDto Occ(int n, string title = "Occ", int? cat = null, bool pinned = false, bool deleted = false,
        DateTime? updated = null, string? notes = null, DateTime? anchor = null) => new()
    {
        SyncId = G(n), Title = title, AnchorDate = anchor ?? T0, Direction = "Since", IsPinned = pinned, Notes = notes,
        CategorySyncId = cat is null ? null : G(cat.Value), UpdatedAt = updated ?? T0, CreatedAt = T0, DeletedAt = deleted ? T0 : null
    };

    public static MilestoneDto Ms(int n, int occ, int days = 100, string label = "M", bool deleted = false, DateTime? updated = null) => new()
    {
        SyncId = G(n), OccasionSyncId = G(occ), ThresholdDays = days, Label = label, UpdatedAt = updated ?? T0, DeletedAt = deleted ? T0 : null
    };

    /// <summary>The same choice for every conflict, bound to the versions those conflicts showed.</summary>
    public static IReadOnlyCollection<ConflictResolution> Pick(IEnumerable<SyncConflict> conflicts, ConflictChoice choice) =>
        conflicts.Select(c => c.Resolve(choice)).ToList();

    public static ExportModel Model(IEnumerable<CategoryDto>? c = null, IEnumerable<OccasionDto>? o = null, IEnumerable<MilestoneDto>? m = null) => new()
    {
        SchemaVersion = "2",
        Categories = (c ?? []).ToList(),
        Occasions = (o ?? []).ToList(),
        Milestones = (m ?? []).ToList(),
    };
}
