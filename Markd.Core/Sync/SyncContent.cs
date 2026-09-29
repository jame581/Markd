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

        public static string Of(OccasionDto o) => Join(
            o.Title, o.Emoji, o.ColorHex, o.IsPinned ? "1" : "0",
            o.AnchorDate.Ticks.ToString(CultureInfo.InvariantCulture), o.Direction, o.Notes,
            o.CategorySyncId?.ToString("N"));

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
