using Markd.Core.Services;

namespace Markd.Core.Sync
{
    public sealed record MergeResult(
        ExportModel Merged, IReadOnlyList<SyncConflict> Conflicts, IReadOnlyDictionary<Guid, Guid> SyncIdRemaps);

    public static class SyncMerger
    {
        public static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(365);

        private const string Dead = "\0dead";

        public static ExportModel Empty() => new() { SchemaVersion = ExportModel.CurrentSchemaVersion };

        /// <summary>
        /// Three-way merge. Pure: no I/O, no clock (nowUtc is a parameter), inputs are never modified.
        /// baseModel is what this device last synced, or null on the first sync.
        /// While Conflicts is non-empty, Merged keeps the local side for the unresolved records and must not be uploaded.
        /// </summary>
        public static MergeResult Merge(
            ExportModel? baseModel, ExportModel local, ExportModel remote, DateTime nowUtc,
            IReadOnlyDictionary<Guid, ConflictChoice>? resolutions = null)
        {
            var choices = resolutions ?? new Dictionary<Guid, ConflictChoice>();
            var remaps = new Dictionary<Guid, Guid>();
            var conflicts = new List<SyncConflict>();

            // Work on clones so the merged records (and later cascades) never alias or change the caller's models.
            local = local.CloneModel();
            remote = remote.CloneModel();
            baseModel = baseModel?.CloneModel();

            if (baseModel is null)
            {
                local = FirstSyncAligner.Align(local, remote, remaps);
                baseModel = Empty();
            }

            var liveOverDeadOccasions = new HashSet<Guid>();
            var categories = MergeKind(baseModel.Categories, local.Categories, remote.Categories,
                c => c.SyncId, SyncContent.Of, c => c.DeletedAt is not null, c => c.UpdatedAt,
                SyncRecordKind.Category, choices, conflicts, null);
            var occasions = MergeKind(baseModel.Occasions, local.Occasions, remote.Occasions,
                o => o.SyncId, SyncContent.Of, o => o.DeletedAt is not null, o => o.UpdatedAt,
                SyncRecordKind.Occasion, choices, conflicts, liveOverDeadOccasions);
            // A milestone edit against a delete of its whole occasion is not worth asking about: the occasion's
            // own decision (or its own conflict) settles it, and the cascade tombstones the orphan.
            var liveOccasions = occasions.Values.Where(o => o.DeletedAt is null).Select(o => o.SyncId).ToHashSet();
            var deadOccasions = local.Occasions.Concat(remote.Occasions).Where(o => o.DeletedAt is not null).Select(o => o.SyncId).ToHashSet();
            var milestones = MergeKind(baseModel.Milestones, local.Milestones, remote.Milestones,
                m => m.SyncId, SyncContent.Of, m => m.DeletedAt is not null, m => m.UpdatedAt,
                SyncRecordKind.Milestone, choices, conflicts, null, m => !liveOccasions.Contains(m.OccasionSyncId),
                m => deadOccasions.Contains(m.OccasionSyncId));

            MergeCascades.Apply(baseModel, local, remote, categories, occasions, milestones, liveOverDeadOccasions, nowUtc);

            var cutoff = nowUtc - TombstoneRetention;
            bool Keep(DateTime? deletedAt) => deletedAt is null || deletedAt >= cutoff;

            var merged = new ExportModel
            {
                SchemaVersion = ExportModel.CurrentSchemaVersion,
                AppVersion = local.AppVersion,
                ExportedAt = nowUtc,
                Categories = categories.Values.Where(c => Keep(c.DeletedAt)).OrderBy(c => c.SyncId).ToList(),
                Occasions = occasions.Values.Where(o => Keep(o.DeletedAt)).OrderBy(o => o.SyncId).ToList(),
                Milestones = milestones.Values.Where(m => Keep(m.DeletedAt)).OrderBy(m => m.SyncId).ToList(),
            };

            return new MergeResult(merged, conflicts, remaps);
        }

        private static Dictionary<Guid, T> MergeKind<T>(
            IEnumerable<T> baseItems, IEnumerable<T> localItems, IEnumerable<T> remoteItems,
            Func<T, Guid> id, Func<T, string> content, Func<T, bool> isDeleted, Func<T, DateTime> updatedAt,
            SyncRecordKind kind, IReadOnlyDictionary<Guid, ConflictChoice> choices, List<SyncConflict> conflicts,
            HashSet<Guid>? liveOverDead, Func<T, bool>? autoDrop = null, Func<T, bool>? parentDeleted = null) where T : class
        {
            var b = baseItems.ToDictionary(id);
            var l = localItems.ToDictionary(id);
            var r = remoteItems.ToDictionary(id);
            var result = new Dictionary<Guid, T>();

            string Sig(T? x) => x is null || isDeleted(x) ? Dead : content(x);
            object? Shown(T? x) => x is null || isDeleted(x) ? null : x;
            DateTime Stamp(T? x) => x is null ? DateTime.MinValue : updatedAt(x);

            foreach (var key in b.Keys.Union(l.Keys).Union(r.Keys).OrderBy(k => k))
            {
                b.TryGetValue(key, out var bItem);
                l.TryGetValue(key, out var lItem);
                r.TryGetValue(key, out var rItem);
                var sb = Sig(bItem);
                var sl = Sig(lItem);
                var sr = Sig(rItem);

                // Without a base record (first sync after a reconnect, a provider switch or a new epoch) a tombstone
                // looks like "never had it" and the live side would win silently. But a SyncId both sides hold was
                // shared once, so a delete on one side against the live record on the other is "deleted vs changed".
                // A tombstone against a record the other side never had stays a plain delete.
                var deleteVersusLive = bItem is null && lItem is not null && rItem is not null && isDeleted(lItem) != isDeleted(rItem);

                T? chosen;
                if (sl == sr)
                {
                    chosen = lItem is null ? rItem : rItem is null ? lItem : updatedAt(rItem) > updatedAt(lItem) ? rItem : lItem;
                }
                else if (!deleteVersusLive && sl == sb)
                {
                    chosen = rItem;
                }
                else if (!deleteVersusLive && sr == sb)
                {
                    chosen = lItem;
                }
                else if (choices.TryGetValue(key, out var choice))
                {
                    chosen = choice == ConflictChoice.Local ? lItem : rItem;
                    var other = choice == ConflictChoice.Local ? rItem : lItem;
                    if (liveOverDead is not null && chosen is not null && !isDeleted(chosen) && (other is null || isDeleted(other)))
                        liveOverDead.Add(key);
                }
                else if (autoDrop is not null && autoDrop((lItem ?? rItem)!))
                {
                    chosen = lItem ?? rItem;   // the cascade tombstones it because its parent is gone
                }
                else if (deleteVersusLive && parentDeleted is not null && parentDeleted(lItem!))
                {
                    // The delete came with its parent's: the parent's own decision settles it, and the cascade
                    // either tombstones it or brings back the live version.
                    chosen = lItem;
                }
                else
                {
                    conflicts.Add(new SyncConflict(key, kind, Shown(lItem), Shown(rItem), Stamp(lItem), Stamp(rItem)));
                    chosen = lItem;
                }

                if (chosen is not null)
                    result[key] = chosen;
            }

            return result;
        }
    }
}
