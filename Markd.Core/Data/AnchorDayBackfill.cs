using Markd.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Data
{
    /// <summary>
    /// Rows from 1.0 have no stored calendar day. Startup fills it from the stored instant in this device's zone,
    /// which is the day the app has been showing for it.
    /// </summary>
    public static class AnchorDayBackfill
    {
        /// <summary>Fills every null AnchorDay, tombstones included. Returns rows changed; a no-op once filled.</summary>
        public static int Run(MarkdDbContext db, TimeZoneInfo? zone = null)
        {
            var missing = db.Occasions.IgnoreQueryFilters().Where(o => o.AnchorDay == null).ToList();
            if (missing.Count == 0)
                return 0;

            foreach (var occasion in missing)
                occasion.AnchorDay = OccasionDates.ToLocalDay(occasion.AnchorDate, zone ?? TimeZoneInfo.Local);

            // Filling in a derived value is not an edit: UpdatedAt stays, so no other device sees a change.
            var stamp = db.StampTimestamps;
            db.StampTimestamps = false;
            try
            {
                db.SaveChanges();
            }
            finally
            {
                db.StampTimestamps = stamp;
                foreach (var occasion in missing)
                    db.Entry(occasion).State = EntityState.Detached;
            }

            return missing.Count;
        }
    }
}
