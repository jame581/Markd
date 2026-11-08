using System.Globalization;

namespace Markd.Core.Services
{
    /// <summary>
    /// A restore on a synced device only adds, so a backup record whose SyncId this device does not know would come
    /// back as a second copy of an event it still has: a 1.0 backup gets fresh SyncIds on upgrade, and a first sync
    /// may have re-identified hand-typed events since a 2.0 backup was made. Such records are matched to live local
    /// ones on content and dropped.
    /// </summary>
    public static class ImportMatcher
    {
        /// <summary>
        /// Returns a copy of <paramref name="model"/> without the records whose SyncId is unknown in
        /// <paramref name="local"/> (tombstones included) and whose content matches a live local record: categories on
        /// name (trimmed, case-insensitive), emoji and colour; occasions on title, day and direction; milestones of a
        /// dropped occasion on threshold and label under the matched local occasion. Kept occasions pointing at a
        /// dropped category point at the matched local one instead. Each local record matches at most one backup record.
        /// </summary>
        public static ExportModel SkipMatches(ExportModel model, ExportModel local) => SkipMatches(model, local, out _);

        /// <param name="skippedOccasions">How many occasions were dropped as matches.</param>
        public static ExportModel SkipMatches(ExportModel model, ExportModel local, out int skippedOccasions)
        {
            var result = model.CloneModel();
            var localIds = local.Categories.Select(c => c.SyncId)
                .Concat(local.Occasions.Select(o => o.SyncId))
                .Concat(local.Milestones.Select(m => m.SyncId))
                .ToHashSet();
            var backupIds = model.Categories.Select(c => c.SyncId)
                .Concat(model.Occasions.Select(o => o.SyncId))
                .Concat(model.Milestones.Select(m => m.SyncId))
                .ToHashSet();

            // A local record the backup also holds by SyncId is already paired with it.
            var categoryMatches = Pair(
                result.Categories.Where(c => c.DeletedAt is null && !localIds.Contains(c.SyncId)),
                local.Categories.Where(c => c.DeletedAt is null && !backupIds.Contains(c.SyncId)),
                c => c.SyncId, c => (Name: Norm(c.Name), c.Emoji, c.ColorHex));

            var occasionMatches = Pair(
                result.Occasions.Where(o => o.DeletedAt is null && !localIds.Contains(o.SyncId)),
                local.Occasions.Where(o => o.DeletedAt is null && !backupIds.Contains(o.SyncId)),
                o => o.SyncId, o => (Title: Norm(o.Title), o.AnchorDay, Direction: Norm(o.Direction)));

            var liveLocalMilestones = local.Milestones.Where(m => m.DeletedAt is null).ToList();
            var milestoneMatches = Pair(
                result.Milestones.Where(m => m.DeletedAt is null && occasionMatches.ContainsKey(m.OccasionSyncId)),
                liveLocalMilestones.Where(m => !backupIds.Contains(m.SyncId)),
                m => m.SyncId,
                m => (Occasion: occasionMatches.TryGetValue(m.OccasionSyncId, out var to) ? to : m.OccasionSyncId,
                      m.ThresholdDays, Label: m.Label.Trim()));

            result.Categories.RemoveAll(c => categoryMatches.ContainsKey(c.SyncId));
            result.Occasions.RemoveAll(o => occasionMatches.ContainsKey(o.SyncId));
            result.Milestones.RemoveAll(m => milestoneMatches.ContainsKey(m.SyncId));
            foreach (var o in result.Occasions)
            {
                if (o.CategorySyncId is { } cid && categoryMatches.TryGetValue(cid, out var to))
                    o.CategorySyncId = to;
            }

            skippedOccasions = occasionMatches.Count;
            return result;
        }

        /// <summary>Backup SyncId to the local SyncId it matches.</summary>
        private static Dictionary<Guid, Guid> Pair<T, TKey>(IEnumerable<T> backup, IEnumerable<T> local, Func<T, Guid> id,
            Func<T, TKey> key) where TKey : notnull
        {
            var localByKey = local.OrderBy(id).GroupBy(key).ToDictionary(g => g.Key, g => new Queue<T>(g));
            var matches = new Dictionary<Guid, Guid>();
            foreach (var b in backup.OrderBy(id))
            {
                if (localByKey.TryGetValue(key(b), out var queue) && queue.Count > 0)
                    matches[id(b)] = id(queue.Dequeue());
            }

            return matches;
        }

        private static string Norm(string s) => s.Trim().ToLower(CultureInfo.InvariantCulture);
    }
}
