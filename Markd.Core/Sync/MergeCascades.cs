using Markd.Core.Services;

namespace Markd.Core.Sync
{
    /// <summary>Rules that connect the three record kinds after each has been merged on its own.</summary>
    internal static class MergeCascades
    {
        public static void Apply(
            ExportModel baseModel, ExportModel local, ExportModel remote,
            Dictionary<Guid, CategoryDto> categories, Dictionary<Guid, OccasionDto> occasions, Dictionary<Guid, MilestoneDto> milestones,
            HashSet<Guid> liveOverDeadCategories, HashSet<Guid> liveOverDeadOccasions, DateTime nowUtc)
        {
            var redirect = CollapseDuplicateCategories(baseModel, categories, nowUtc);
            RestoreCategoryMemberships(baseModel, local, remote, categories, occasions, liveOverDeadCategories, redirect, nowUtc);
            RepointCategories(categories, occasions, redirect, nowUtc);
            FollowOccasions(local, remote, occasions, milestones, liveOverDeadOccasions, nowUtc);
            KeepOnePin(occasions, nowUtc);
        }

        /// <summary>
        /// Live categories with the same name (any case), emoji and colour are indistinguishable, so they become one.
        /// Only groups that contain a category that is new since the base are touched: duplicates the user already had
        /// on one device are left alone.
        /// </summary>
        private static Dictionary<Guid, Guid> CollapseDuplicateCategories(ExportModel baseModel, Dictionary<Guid, CategoryDto> categories, DateTime nowUtc)
        {
            var redirect = new Dictionary<Guid, Guid>();
            var baseIds = baseModel.Categories.Where(c => c.DeletedAt is null).Select(c => c.SyncId).ToHashSet();
            var groups = categories.Values
                .Where(c => c.DeletedAt is null)
                .GroupBy(c => (Name: c.Name.Trim().ToLowerInvariant(), c.Emoji, c.ColorHex))
                .Where(g => g.Count() > 1 && g.Any(c => !baseIds.Contains(c.SyncId)))
                .ToList();

            foreach (var group in groups)
            {
                var keeper = group.Min(c => c.SyncId);
                foreach (var duplicate in group.Where(c => c.SyncId != keeper).ToList())
                {
                    redirect[duplicate.SyncId] = keeper;
                    var tombstone = duplicate.Clone();
                    tombstone.DeletedAt = nowUtc;
                    tombstone.UpdatedAt = nowUtc;
                    categories[duplicate.SyncId] = tombstone;
                }
            }

            return redirect;
        }

        /// <summary>
        /// The user kept a category against a delete: the delete had also cleared its occasions' category, and that
        /// side effect merged without a conflict. Put back each occasion that was in it before and still is on the
        /// keeping side. An occasion whose core came from neither side, or that was moved elsewhere, is left alone.
        /// A kept category that collapsed into a duplicate still counts: its occasions get its id back and
        /// <see cref="RepointCategories"/> moves them to the duplicate.
        /// </summary>
        private static void RestoreCategoryMemberships(
            ExportModel baseModel, ExportModel local, ExportModel remote,
            Dictionary<Guid, CategoryDto> categories, Dictionary<Guid, OccasionDto> occasions,
            HashSet<Guid> liveOverDeadCategories, Dictionary<Guid, Guid> redirect, DateTime nowUtc)
        {
            if (liveOverDeadCategories.Count == 0)
                return;

            var baseOccasions = baseModel.Occasions.ToDictionary(o => o.SyncId);
            var localOccasions = local.Occasions.ToDictionary(o => o.SyncId);
            var remoteOccasions = remote.Occasions.ToDictionary(o => o.SyncId);
            foreach (var categoryId in liveOverDeadCategories)
            {
                if (!categories.TryGetValue(categoryId, out var category)
                    || (category.DeletedAt is not null && !redirect.ContainsKey(categoryId)))
                    continue;
                var keptLocally = local.Categories.Any(c => c.SyncId == categoryId && c.DeletedAt is null);
                var kept = keptLocally ? localOccasions : remoteOccasions;

                foreach (var key in occasions.Keys.ToList())
                {
                    var occasion = occasions[key];
                    if (occasion.DeletedAt is not null || occasion.CategorySyncId is not null
                        || !baseOccasions.TryGetValue(key, out var before) || before.CategorySyncId != categoryId
                        || !kept.TryGetValue(key, out var keeper) || keeper.DeletedAt is not null || keeper.CategorySyncId != categoryId)
                        continue;

                    var core = SyncContent.CoreOf(occasion);
                    var fromASide = (localOccasions.TryGetValue(key, out var l) && SyncContent.CoreOf(l) == core)
                                    || (remoteOccasions.TryGetValue(key, out var r) && SyncContent.CoreOf(r) == core);
                    if (!fromASide)
                        continue;

                    var copy = occasion.Clone();
                    copy.CategorySyncId = categoryId;
                    copy.UpdatedAt = nowUtc;
                    occasions[key] = copy;
                }
            }
        }

        /// <summary>An occasion never keeps a reference to a dead or missing category. It becomes uncategorised.</summary>
        private static void RepointCategories(
            Dictionary<Guid, CategoryDto> categories, Dictionary<Guid, OccasionDto> occasions,
            Dictionary<Guid, Guid> redirect, DateTime nowUtc)
        {
            var live = categories.Values.Where(c => c.DeletedAt is null).Select(c => c.SyncId).ToHashSet();
            foreach (var key in occasions.Keys.ToList())
            {
                var occasion = occasions[key];
                if (occasion.DeletedAt is not null || occasion.CategorySyncId is not { } current)
                    continue;

                var target = redirect.TryGetValue(current, out var to) ? to : current;
                Guid? fixedId = live.Contains(target) ? target : null;
                if (fixedId == occasion.CategorySyncId)
                    continue;

                var copy = occasion.Clone();
                copy.CategorySyncId = fixedId;
                copy.UpdatedAt = nowUtc;
                occasions[key] = copy;
            }
        }

        private static void FollowOccasions(
            ExportModel local, ExportModel remote,
            Dictionary<Guid, OccasionDto> occasions, Dictionary<Guid, MilestoneDto> milestones,
            HashSet<Guid> liveOverDeadOccasions, DateTime nowUtc)
        {
            var liveOccasions = occasions.Values.Where(o => o.DeletedAt is null).Select(o => o.SyncId).ToHashSet();

            // Milestones of a dead or missing occasion die with it.
            foreach (var key in milestones.Keys.ToList())
            {
                var milestone = milestones[key];
                if (milestone.DeletedAt is null && !liveOccasions.Contains(milestone.OccasionSyncId))
                {
                    var tombstone = milestone.Clone();
                    tombstone.DeletedAt = nowUtc;
                    tombstone.UpdatedAt = nowUtc;
                    milestones[key] = tombstone;
                }
            }

            // The user kept an occasion against a delete: the delete had also tombstoned its milestones, and
            // losing those silently would be data loss. Bring back whichever live version exists.
            if (liveOverDeadOccasions.Count == 0)
                return;

            var candidates = local.Milestones.Concat(remote.Milestones)
                .Where(m => m.DeletedAt is null && liveOverDeadOccasions.Contains(m.OccasionSyncId))
                .GroupBy(m => m.SyncId);
            foreach (var group in candidates)
            {
                if (milestones.TryGetValue(group.Key, out var current) && current.DeletedAt is null)
                    continue;

                milestones[group.Key] = group.OrderByDescending(m => m.UpdatedAt).First().Clone();
            }
        }

        private static void KeepOnePin(Dictionary<Guid, OccasionDto> occasions, DateTime nowUtc)
        {
            var pinned = occasions.Values
                .Where(o => o.DeletedAt is null && o.IsPinned)
                .OrderByDescending(o => o.UpdatedAt)
                .ThenBy(o => o.SyncId)
                .ToList();

            foreach (var extra in pinned.Skip(1))
            {
                var copy = extra.Clone();
                copy.IsPinned = false;
                copy.UpdatedAt = nowUtc;
                occasions[extra.SyncId] = copy;
            }
        }
    }
}
