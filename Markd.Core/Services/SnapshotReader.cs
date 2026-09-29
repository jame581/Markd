using Markd.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Services
{
    internal static class SnapshotReader
    {
        /// <summary>
        /// Reads the whole dataset as a v2 model. Sync reads tombstones and drops Notified (device-local).
        /// Manual export reads live records only and keeps Notified and the local int ids (SourceId).
        /// Sync snapshots carry SourceId 0: local ids mean nothing on another device.
        /// </summary>
        public static async Task<ExportModel> ReadAsync(MarkdDbContext db, bool includeTombstones, bool includeNotified)
        {
            var model = new ExportModel { SchemaVersion = ExportModel.CurrentSchemaVersion, ExportedAt = DateTime.UtcNow };

            var categories = await db.Categories.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            var occasions = await db.Occasions.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            var milestones = await db.Milestones.IgnoreQueryFilters().AsNoTracking().ToListAsync();

            var categorySync = categories.ToDictionary(c => c.Id, c => c.SyncId);
            var liveCategories = categories.Where(c => c.DeletedAt is null).Select(c => c.Id).ToHashSet();
            var occasionSync = new Dictionary<int, Guid>();

            foreach (var c in categories.Where(c => includeTombstones || c.DeletedAt is null))
            {
                model.Categories.Add(new CategoryDto
                {
                    SourceId = includeNotified ? c.Id : 0, SyncId = c.SyncId, UpdatedAt = c.UpdatedAt, DeletedAt = c.DeletedAt,
                    Name = c.Name, Emoji = c.Emoji, ColorHex = c.ColorHex
                });
            }

            foreach (var o in occasions.Where(o => includeTombstones || o.DeletedAt is null))
            {
                occasionSync[o.Id] = o.SyncId;
                model.Occasions.Add(new OccasionDto
                {
                    SourceId = includeNotified ? o.Id : 0, SyncId = o.SyncId, UpdatedAt = o.UpdatedAt, DeletedAt = o.DeletedAt,
                    Title = o.Title, Emoji = o.Emoji, ColorHex = o.ColorHex, IsPinned = o.IsPinned,
                    AnchorDate = o.AnchorDate, Direction = o.Direction.ToString(), Notes = o.Notes,
                    CreatedAt = o.CreatedAt, CategorySourceId = includeNotified ? o.CategoryId : null,
                    CategorySyncId = o.CategoryId is { } cid && (includeTombstones || liveCategories.Contains(cid)) && categorySync.TryGetValue(cid, out var g) ? g : null
                });
            }

            foreach (var m in milestones.Where(m => includeTombstones || m.DeletedAt is null))
            {
                if (!occasionSync.TryGetValue(m.OccasionId, out var occasionSyncId))
                    continue;

                model.Milestones.Add(new MilestoneDto
                {
                    SourceId = includeNotified ? m.Id : 0, SyncId = m.SyncId, UpdatedAt = m.UpdatedAt, DeletedAt = m.DeletedAt,
                    OccasionSourceId = includeNotified ? m.OccasionId : 0, OccasionSyncId = occasionSyncId,
                    ThresholdDays = m.ThresholdDays, Label = m.Label, Notified = includeNotified && m.Notified
                });
            }

            return model;
        }
    }
}
