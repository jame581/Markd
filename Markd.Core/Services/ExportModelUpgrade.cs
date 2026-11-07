using Markd.Core.Domain;

namespace Markd.Core.Services
{
    public static class ExportModelUpgrade
    {
        /// <summary>
        /// Returns a v2 model. A v1 file gets fresh SyncIds, its int references rewritten and each anchor instant turned
        /// into the calendar day it shows in <paramref name="zone"/> (this device's zone by default). v2 is returned as is.
        /// </summary>
        public static ExportModel ToCurrent(ExportModel model, TimeZoneInfo? zone = null)
        {
            if (model.SchemaVersion == ExportModel.CurrentSchemaVersion)
                return model;

            var now = DateTime.UtcNow;
            var upgraded = new ExportModel
            {
                SchemaVersion = ExportModel.CurrentSchemaVersion,
                AppVersion = model.AppVersion,
                ExportedAt = model.ExportedAt,
                DeviceId = model.DeviceId,
                Settings = model.Settings,
            };

            var categoryIds = new Dictionary<int, Guid>();
            foreach (var c in model.Categories)
            {
                var copy = c.Clone();
                copy.SyncId = Guid.NewGuid();
                copy.UpdatedAt = now;
                copy.DeletedAt = null;
                categoryIds[c.SourceId] = copy.SyncId;
                upgraded.Categories.Add(copy);
            }

            var occasionIds = new Dictionary<int, Guid>();
            foreach (var o in model.Occasions)
            {
                var copy = o.Clone();
                copy.SyncId = Guid.NewGuid();
                copy.AnchorDay = OccasionDates.ToLocalDay(o.AnchorDate, zone);
                copy.AnchorDate = default;
                copy.UpdatedAt = o.CreatedAt == default ? now : o.CreatedAt;
                copy.DeletedAt = null;
                copy.CategorySyncId = o.CategorySourceId is { } cid && categoryIds.TryGetValue(cid, out var g) ? g : null;
                occasionIds[o.SourceId] = copy.SyncId;
                upgraded.Occasions.Add(copy);
            }

            foreach (var m in model.Milestones)
            {
                if (!occasionIds.TryGetValue(m.OccasionSourceId, out var oid))
                    continue;

                var copy = m.Clone();
                copy.SyncId = Guid.NewGuid();
                copy.UpdatedAt = now;
                copy.DeletedAt = null;
                copy.OccasionSyncId = oid;
                upgraded.Milestones.Add(copy);
            }

            return upgraded;
        }
    }
}
