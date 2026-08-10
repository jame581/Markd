using Markd.Core.Data;
using Markd.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Services
{
    public class OccasionService(MarkdDbContext db) : IOccasionService
    {
        public async Task<Occasion> CreateAsync(Occasion occasion)
        {
            occasion.CreatedAt = DateTime.UtcNow;

            if (occasion.IsPinned)
                await UnpinAllAsync();

            db.Occasions.Add(occasion);
            await db.SaveChangesAsync();
            return occasion;
        }

        public async Task DeleteAsync(int id)
        {
            var occasion = await db.Occasions.FindAsync(id);
            if (occasion == null)
                return;

            db.Occasions.Remove(occasion);
            await db.SaveChangesAsync();
        }

        public async Task<List<Occasion>> GetAllAsync()
        {
            return await db.Occasions
                .Include(o => o.Category)
                .Include(o => o.Milestones)
                .OrderBy(o => o.Title)
                .ToListAsync();
        }

        public async Task<Occasion?> GetByIdAsync(int id)
        {
            return await db.Occasions
                .Include(o => o.Category)
                .Include(o => o.Milestones)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public int GetDays(Occasion occasion)
        {
            var today = DateTime.UtcNow.Date;
            var anchor = occasion.AnchorDate.Date;

            return occasion.Direction switch
            {
                OccasionDirection.Since => (today - anchor).Days,
                OccasionDirection.Until => (anchor - today).Days,
                _ => 0
            };
        }

        public async Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync()
        {
            var occasions = await db.Occasions
                .Include(o => o.Milestones)
                .Where(o => o.Direction == OccasionDirection.Since)
                .ToListAsync();

            var pending = new List<(Occasion, Milestone)>();

            foreach (var occasion in occasions)
            {
                var days = GetDays(occasion);
                var hit = occasion.Milestones.Where(m => !m.Notified && m.ThresholdDays <= days);

                foreach (var milestone in hit)
                    pending.Add((occasion, milestone));
            }

            return pending;
        }

        public async Task<Occasion> UpdateAsync(Occasion occasion)
        {
            if (occasion.IsPinned)
                await UnpinAllAsync(occasion.Id);

            db.Occasions.Update(occasion);
            await db.SaveChangesAsync();
            return occasion;
        }

        public async Task SetPinnedAsync(int id)
        {
            await UnpinAllAsync(id);

            var occasion = await db.Occasions.FindAsync(id);
            if (occasion == null)
                return;

            occasion.IsPinned = true;
            await db.SaveChangesAsync();
        }

        private async Task UnpinAllAsync(int? keepId = null)
        {
            var pinned = await db.Occasions
                .Where(o => o.IsPinned && (!keepId.HasValue || o.Id != keepId.Value))
                .ToListAsync();

            foreach (var occasion in pinned)
                occasion.IsPinned = false;
        }
    }
}
