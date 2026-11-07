using System.Globalization;
using Markd.Core.Services;

namespace Markd.Core.Sync
{
    /// <summary>
    /// What "the same record" means across devices. Timestamps are deliberately excluded: clock skew must
    /// never create or hide a conflict. Notified is device-local and excluded too.
    /// </summary>
    public static class SyncContent
    {
        private const char Sep = '\u001f';

        public static string Of(CategoryDto c) => Join(c.Name, c.Emoji, c.ColorHex);

        public static string Of(OccasionDto o)
        {
            var core = CoreParts(o);
            return Join([.. core[..3], o.IsPinned ? "1" : "0", .. core[3..], o.CategorySyncId?.ToString("N")]);
        }

        /// <summary>An occasion without its pin and category, which merge on their own.</summary>
        public static string CoreOf(OccasionDto o) => Join(CoreParts(o));

        // Of keeps its original field order (pin after the colour, category last), so fingerprints do not change.
        private static string?[] CoreParts(OccasionDto o) =>
        [
            o.Title, o.Emoji, o.ColorHex,
            o.AnchorDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), o.Direction, o.Notes,
        ];

        public static string Of(MilestoneDto m) => Join(
            m.OccasionSyncId.ToString("N"), m.ThresholdDays.ToString(CultureInfo.InvariantCulture), m.Label);

        /// <summary>Equal fingerprints mean two snapshots hold the same live data. Tombstones and timestamps do not count.</summary>
        public static string Fingerprint(ExportModel m)
        {
            var lines = m.Categories.Where(c => c.DeletedAt is null).Select(c => "C" + c.SyncId.ToString("N") + Sep + Of(c))
                .Concat(m.Occasions.Where(o => o.DeletedAt is null).Select(o => "O" + o.SyncId.ToString("N") + Sep + Of(o)))
                .Concat(m.Milestones.Where(x => x.DeletedAt is null).Select(x => "M" + x.SyncId.ToString("N") + Sep + Of(x)))
                .OrderBy(s => s, StringComparer.Ordinal);
            return string.Join('\n', lines);
        }

        private static string Join(params string?[] parts) => string.Join(Sep, parts.Select(p => p ?? "\0"));
    }
}
