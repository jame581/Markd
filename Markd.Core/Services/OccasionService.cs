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

        public async Task DeleteAllAsync()
        {
            await using var transaction = await db.Database.BeginTransactionAsync();

            await db.Milestones.ExecuteDeleteAsync();
            await db.Occasions.ExecuteDeleteAsync();

            await transaction.CommitAsync();
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

        public async Task<IReadOnlyList<CalendarMark>> GetCalendarMarksAsync(int year, int month)
        {
            var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEnd = monthStart.AddMonths(1);

            var anchorMarks = await db.Occasions
                .AsNoTracking()
                .Where(o => o.AnchorDate >= monthStart && o.AnchorDate < monthEnd)
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    o.Emoji,
                    o.ColorHex,
                    o.AnchorDate
                })
                .ToListAsync();

            var milestoneMarks = await db.Occasions
                .AsNoTracking()
                .Join(
                    db.Milestones.AsNoTracking(),
                    occasion => occasion.Id,
                    milestone => milestone.OccasionId,
                    (occasion, milestone) => new
                    {
                        occasion.Id,
                        occasion.Title,
                        occasion.Emoji,
                        occasion.ColorHex,
                        occasion.Direction,
                        occasion.AnchorDate,
                        milestone.Label,
                        milestone.ThresholdDays,
                        milestone.Notified
                    })
                .Select(mark => new
                {
                    mark.Id,
                    mark.Title,
                    mark.Emoji,
                    mark.ColorHex,
                    Date = mark.Direction == OccasionDirection.Since
                        ? mark.AnchorDate.AddDays(mark.ThresholdDays)
                        : mark.AnchorDate.AddDays(-mark.ThresholdDays),
                    mark.Label,
                    mark.ThresholdDays,
                    mark.Notified
                })
                .Where(mark => mark.Date >= monthStart && mark.Date < monthEnd)
                .ToListAsync();

            var marks = anchorMarks
                .Select(mark => new CalendarMark(
                    DateOnly.FromDateTime(mark.AnchorDate),
                    mark.Id,
                    mark.Title,
                    mark.Emoji,
                    mark.ColorHex,
                    CalendarMarkKind.Anchor,
                    null,
                    null,
                    false))
                .Concat(milestoneMarks.Select(mark => new CalendarMark(
                    DateOnly.FromDateTime(mark.Date),
                    mark.Id,
                    mark.Title,
                    mark.Emoji,
                    mark.ColorHex,
                    CalendarMarkKind.Milestone,
                    mark.Label,
                    mark.ThresholdDays,
                    mark.Notified)))
                .OrderBy(mark => mark.Date)
                .ThenBy(mark => mark.Kind)
                .ThenBy(mark => mark.Title)
                .ToList();

            return marks;
        }

        public async Task<Occasion> UpdateAsync(Occasion occasion)
        {
            if (occasion.IsPinned)
                await UnpinAllAsync(occasion.Id);

            var existing = await db.Occasions
                .FirstOrDefaultAsync(o => o.Id == occasion.Id);

            if (existing == null)
                throw new InvalidOperationException($"Occasion {occasion.Id} was not found.");

            existing.Title = occasion.Title;
            existing.Emoji = occasion.Emoji;
            existing.ColorHex = occasion.ColorHex;
            existing.AnchorDate = occasion.AnchorDate;
            existing.Direction = occasion.Direction;
            existing.Notes = occasion.Notes;
            existing.IsPinned = occasion.IsPinned;
            existing.CategoryId = occasion.CategoryId;

            await db.SaveChangesAsync();
            return existing;
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

        public async Task<Milestone> AddMilestoneAsync(int occasionId, int thresholdDays, string label)
        {
            var milestone = new Milestone
            {
                OccasionId = occasionId,
                ThresholdDays = thresholdDays,
                Label = label
            };

            db.Milestones.Add(milestone);
            await db.SaveChangesAsync();
            return milestone;
        }

        public async Task RemoveMilestoneAsync(int milestoneId)
        {
            var milestone = await db.Milestones.FindAsync(milestoneId);
            if (milestone == null)
                return;

            db.Milestones.Remove(milestone);
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

        public async Task MarkMilestoneNotifiedAsync(int milestoneId)
        {
            var milestone = await db.Milestones.FindAsync(milestoneId);
            if (milestone is null) return;
            milestone.Notified = true;
            await db.SaveChangesAsync();
        }
    }
}
