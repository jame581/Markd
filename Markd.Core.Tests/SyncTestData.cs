using Markd.Core.Services;
using Markd.Core.Sync;

namespace Markd.Core.Tests;

internal static class Zones
{
    /// <summary>A fixed-offset zone without daylight saving (no OS zone ids, so tests run anywhere).</summary>
    public static TimeZoneInfo Utc(int hours)
    {
        var name = hours >= 0 ? $"UTC+{hours}" : $"UTC{hours}";
        return TimeZoneInfo.CreateCustomTimeZone(name, TimeSpan.FromHours(hours), name, name);
    }

    /// <summary>
    /// UTC-5 with daylight saving that starts at local midnight on 2 March, so 00:00 to 01:00 that night does not exist.
    /// </summary>
    public static TimeZoneInfo MidnightGap()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 2);
        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Gap", TimeSpan.FromHours(-5), "Gap", "Gap", "Gap DST", [rule]);
    }
}

internal static class Snap
{
    public static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Now = T0.AddDays(10);
    public static readonly DateOnly D0 = new(2026, 1, 1);

    public static Guid G(int n) => new(n, 0, 0, new byte[8]);

    public static CategoryDto Cat(int n, string name = "Cat", bool deleted = false, DateTime? updated = null) => new()
    {
        SyncId = G(n), Name = name, UpdatedAt = updated ?? T0, DeletedAt = deleted ? T0 : null
    };

    public static OccasionDto Occ(int n, string title = "Occ", int? cat = null, bool pinned = false, bool deleted = false,
        DateTime? updated = null, string? notes = null, DateOnly? day = null) => new()
    {
        SyncId = G(n), Title = title, AnchorDay = day ?? D0, Direction = "Since", IsPinned = pinned, Notes = notes,
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
