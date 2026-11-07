using Markd.Core.Services;

namespace Markd.Core.Sync
{
    /// <summary>
    /// Merges an occasion as three parts: its core (<see cref="SyncContent.CoreOf"/>), its pin and its category.
    /// Pin and category often change as side effects (pinning another occasion, deleting a category), so they must not
    /// turn an unrelated edit into a conflict. A category that is dead after the merge reads as none on every side.
    /// </summary>
    internal static class OccasionFieldMerge
    {
        private readonly record struct Part<TValue>(bool Resolved, TValue Value, bool FromRemote);

        /// <summary>
        /// Base, local and remote are all live and both sides changed. Returns the combined occasion when every part
        /// resolves on its own, or null when any part was changed differently on both sides.
        /// </summary>
        public static OccasionDto? TryCombine(OccasionDto baseItem, OccasionDto local, OccasionDto remote, IReadOnlySet<Guid> liveCategories)
        {
            var core = Merge(SyncContent.CoreOf(baseItem), SyncContent.CoreOf(local), SyncContent.CoreOf(remote));
            var pin = Merge(baseItem.IsPinned, local.IsPinned, remote.IsPinned);
            var category = Merge(Category(baseItem, liveCategories), Category(local, liveCategories), Category(remote, liveCategories));
            if (!core.Resolved || !pin.Resolved || !category.Resolved)
                return null;

            var combined = (core.FromRemote ? remote : local).Clone();
            combined.IsPinned = pin.Value;
            combined.CategorySyncId = category.Value;
            combined.UpdatedAt = Max(local.UpdatedAt, remote.UpdatedAt);
            return combined;
        }

        /// <summary>The live side only moved the pin or the category, which a delete makes moot.</summary>
        public static bool IsSideEffectOnly(OccasionDto baseItem, OccasionDto live) =>
            SyncContent.CoreOf(baseItem) == SyncContent.CoreOf(live);

        /// <summary>
        /// A conflict choice applies to the core: the pin and category keep whatever resolved without a conflict, and
        /// follow the choice only when they conflicted too.
        /// </summary>
        public static OccasionDto ApplyChoice(OccasionDto baseItem, OccasionDto chosen, OccasionDto other, IReadOnlySet<Guid> liveCategories)
        {
            var pin = Merge(baseItem.IsPinned, chosen.IsPinned, other.IsPinned);
            var category = Merge(Category(baseItem, liveCategories), Category(chosen, liveCategories), Category(other, liveCategories));
            var isPinned = pin.Resolved ? pin.Value : chosen.IsPinned;
            var categorySyncId = category.Resolved ? category.Value : chosen.CategorySyncId;
            if (isPinned == chosen.IsPinned && categorySyncId == chosen.CategorySyncId)
                return chosen;

            var result = chosen.Clone();
            result.IsPinned = isPinned;
            result.CategorySyncId = categorySyncId;
            result.UpdatedAt = Max(chosen.UpdatedAt, other.UpdatedAt);
            return result;
        }

        private static Guid? Category(OccasionDto o, IReadOnlySet<Guid> liveCategories) =>
            o.CategorySyncId is { } id && liveCategories.Contains(id) ? id : null;

        /// <summary>"First" plays local, "second" remote: one side changed it, or both made the same change.</summary>
        private static Part<TValue> Merge<TValue>(TValue baseValue, TValue first, TValue second)
        {
            var same = EqualityComparer<TValue>.Default;
            if (same.Equals(first, second) || same.Equals(second, baseValue))
                return new(true, first, false);
            if (same.Equals(first, baseValue))
                return new(true, second, true);
            return new(false, first, false);
        }

        private static DateTime Max(DateTime a, DateTime b) => a >= b ? a : b;
    }
}
