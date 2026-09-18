using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Localization;
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

        public async Task<Occasion> RestoreAsync(Occasion snapshot)
        {
            if (snapshot.IsPinned)
                await UnpinAllAsync();

            var occasion = new Occasion
            {
                Title = snapshot.Title,
                Emoji = snapshot.Emoji,
                ColorHex = snapshot.ColorHex,
                IsPinned = snapshot.IsPinned,
                AnchorDate = snapshot.AnchorDate,
                Direction = snapshot.Direction,
                Notes = snapshot.Notes,
                CreatedAt = snapshot.CreatedAt == default ? DateTime.UtcNow : snapshot.CreatedAt,
                CategoryId = snapshot.CategoryId is { } categoryId && await db.Categories.AnyAsync(c => c.Id == categoryId)
                    ? categoryId
                    : null,
                Milestones = snapshot.Milestones
                    .Select(m => new Milestone { ThresholdDays = m.ThresholdDays, Label = m.Label, Notified = m.Notified })
                    .ToList()
            };

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

        public int GetDays(Occasion occasion) => OccasionDates.GetDays(occasion, DateTime.Now);

        public async Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync()
        {
            var occasions = await db.Occasions
                .Include(o => o.Milestones)
                .ToListAsync();

            var pending = new List<(Occasion, Milestone)>();

            foreach (var occasion in occasions)
            {
                var days = GetDays(occasion);
                var hit = occasion.Milestones
                    .Where(m => !m.Notified && OccasionDates.IsMilestoneReached(occasion, m, days))
                    .OrderBy(m => m.ThresholdDays);

                foreach (var milestone in hit)
                    pending.Add((occasion, milestone));
            }

            return pending;
        }

        public async Task<IReadOnlyList<CalendarMark>> GetCalendarMarksAsync(int year, int month)
        {
            var monthStartLocal = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Local);
            var monthEndLocal = monthStartLocal.AddMonths(1);
            var monthStartUtc = monthStartLocal.ToUniversalTime();
            var monthEndUtc = monthEndLocal.ToUniversalTime();

            var anchorMarks = await db.Occasions
                .AsNoTracking()
                .Where(o => o.AnchorDate >= monthStartUtc && o.AnchorDate < monthEndUtc)
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    o.Emoji,
                    o.ColorHex,
                    o.AnchorDate
                })
                .ToListAsync();

            // Milestone dates are local calendar dates (anchor date ± threshold). Adding days to the stored UTC instant
            // drifts by a day across a daylight-saving change, so the dates are computed in memory, not in SQL.
            var monthStart = DateOnly.FromDateTime(monthStartLocal);
            var monthEnd = DateOnly.FromDateTime(monthEndLocal);
            var milestoneMarks = (await db.Milestones
                    .AsNoTracking()
                    .Include(m => m.Occasion)
                    .ToListAsync())
                .Select(milestone => new
                {
                    milestone.Occasion!.Id,
                    milestone.Occasion.Title,
                    milestone.Occasion.Emoji,
                    milestone.Occasion.ColorHex,
                    Date = DateOnly.FromDateTime(OccasionDates.GetMilestoneDate(milestone.Occasion, milestone)),
                    milestone.Label,
                    milestone.ThresholdDays,
                    milestone.Notified
                })
                .Where(mark => mark.Date >= monthStart && mark.Date < monthEnd)
                .ToList();

            var marks = anchorMarks
                .Select(mark => new CalendarMark(
                    DateOnly.FromDateTime(DateTime.SpecifyKind(mark.AnchorDate, DateTimeKind.Utc).ToLocalTime()),
                    mark.Id,
                    mark.Title,
                    mark.Emoji,
                    mark.ColorHex,
                    CalendarMarkKind.Anchor,
                    null,
                    null,
                    false))
                .Concat(milestoneMarks.Select(mark => new CalendarMark(
                    mark.Date,
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
                throw new InvalidOperationException(string.Format(LocalizationManager.Instance.Culture, Strings.Occasion_NotFoundById, occasion.Id));

            var datesChanged = existing.AnchorDate != occasion.AnchorDate || existing.Direction != occasion.Direction;

            existing.Title = occasion.Title;
            existing.Emoji = occasion.Emoji;
            existing.ColorHex = occasion.ColorHex;
            existing.AnchorDate = occasion.AnchorDate;
            existing.Direction = occasion.Direction;
            existing.Notes = occasion.Notes;
            existing.IsPinned = occasion.IsPinned;
            existing.CategoryId = occasion.CategoryId;

            // Moving the anchor or flipping the direction re-dates every milestone. Reached ones count as announced
            // (the user just chose the date, as when adding a milestone that has already passed); the rest will
            // notify again when they land.
            if (datesChanged)
            {
                await db.Entry(existing).Collection(o => o.Milestones).LoadAsync();
                var days = GetDays(existing);
                foreach (var milestone in existing.Milestones)
                    milestone.Notified = OccasionDates.IsMilestoneReached(existing, milestone, days);
            }

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
