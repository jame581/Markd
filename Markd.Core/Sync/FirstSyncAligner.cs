using System.Globalization;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    /// <summary>
    /// A device joining a cloud file that already has its own data (typically the same events typed by hand on
    /// both devices) must not end up with every event twice. Records that mean the same thing adopt the
    /// remote SyncId, and the caller renames the local rows in place so nothing is recreated (which would
    /// also reset the local Notified flags and re-fire notifications).
    /// </summary>
    internal static class FirstSyncAligner
    {
        public static ExportModel Align(ExportModel local, ExportModel remote, Dictionary<Guid, Guid> remaps)
        {
            var result = local.CloneModel();

            var remoteIds = remote.Categories.Select(c => c.SyncId)
                .Concat(remote.Occasions.Select(o => o.SyncId))
                .Concat(remote.Milestones.Select(m => m.SyncId))
                .ToHashSet();
            var localIds = local.Categories.Select(c => c.SyncId)
                .Concat(local.Occasions.Select(o => o.SyncId))
                .Concat(local.Milestones.Select(m => m.SyncId))
                .ToHashSet();

            Pair(result.Categories.Where(c => c.DeletedAt is null && !remoteIds.Contains(c.SyncId)),
                 remote.Categories.Where(c => c.DeletedAt is null && !localIds.Contains(c.SyncId)),
                 c => c.SyncId, c => (Name: Norm(c.Name), c.Emoji, c.ColorHex), remaps);   // the identity MergeCascades collapses on

            Pair(result.Occasions.Where(o => o.DeletedAt is null && !remoteIds.Contains(o.SyncId)),
                 remote.Occasions.Where(o => o.DeletedAt is null && !localIds.Contains(o.SyncId)),
                 o => o.SyncId, o => Norm(o.Title) + "|" + o.AnchorDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + o.Direction, remaps);

            foreach (var c in result.Categories) c.SyncId = Map(remaps, c.SyncId);
            foreach (var o in result.Occasions)
            {
                o.SyncId = Map(remaps, o.SyncId);
                if (o.CategorySyncId is { } cid) o.CategorySyncId = Map(remaps, cid);
            }
            foreach (var m in result.Milestones) m.OccasionSyncId = Map(remaps, m.OccasionSyncId);

            // Milestones pair up under their (now aligned) occasion.
            var localAfter = result.Milestones.Select(m => m.SyncId).ToHashSet();
            var milestoneRemaps = new Dictionary<Guid, Guid>();
            Pair(result.Milestones.Where(m => m.DeletedAt is null && !remoteIds.Contains(m.SyncId)),
                 remote.Milestones.Where(m => m.DeletedAt is null && !localAfter.Contains(m.SyncId)),
                 m => m.SyncId, m => m.OccasionSyncId.ToString("N") + "|" + m.ThresholdDays + "|" + Norm(m.Label), milestoneRemaps);
            foreach (var m in result.Milestones) m.SyncId = Map(milestoneRemaps, m.SyncId);
            foreach (var pair in milestoneRemaps) remaps[pair.Key] = pair.Value;

            return result;
        }

        private static void Pair<T, TKey>(IEnumerable<T> locals, IEnumerable<T> remotes, Func<T, Guid> id, Func<T, TKey> key,
            Dictionary<Guid, Guid> remaps) where TKey : notnull
        {
            var remoteByKey = remotes.OrderBy(id).GroupBy(key).ToDictionary(g => g.Key, g => new Queue<T>(g));
            foreach (var l in locals.OrderBy(id))
            {
                if (remoteByKey.TryGetValue(key(l), out var queue) && queue.Count > 0)
                    remaps[id(l)] = id(queue.Dequeue());
            }
        }

        private static Guid Map(Dictionary<Guid, Guid> map, Guid id) => map.TryGetValue(id, out var to) ? to : id;

        private static string Norm(string s) => s.Trim().ToLowerInvariant();
    }
}
