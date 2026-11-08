using Markd.Core.Data;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Services
{
    /// <param name="zone">The device's time zone, which turns stored anchors into calendar days and back. Defaults to local.</param>
    public class SnapshotService(MarkdDbContext db, TimeZoneInfo? zone = null) : ISnapshotService
    {
        private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

        public TimeZoneInfo Zone => _zone;

        public Task<ExportModel> ReadForSyncAsync() =>
            SnapshotReader.ReadAsync(db, includeTombstones: true, includeNotified: false, _zone);

        public async Task ApplyAsync(ExportModel target, SnapshotApplyOptions options)
        {
            var now = options.NowUtc ?? DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.StampTimestamps = false;

                if (options.ExpectedLocalFingerprint is { } expected)
                {
                    var current = await SnapshotReader.ReadAsync(db, includeTombstones: true, includeNotified: false, _zone);
                    if (Markd.Core.Sync.SyncContent.Fingerprint(current) != expected)
                        throw new SnapshotChangedException();
                }

                var categories = await db.Categories.IgnoreQueryFilters().ToListAsync();
                var occasions = await db.Occasions.IgnoreQueryFilters().ToListAsync();
                var milestones = await db.Milestones.IgnoreQueryFilters().ToListAsync();

                if (options.SyncIdRemaps is { Count: > 0 } remaps)
                {
                    foreach (var c in categories) if (remaps.TryGetValue(c.SyncId, out var to)) c.SyncId = to;
                    foreach (var o in occasions) if (remaps.TryGetValue(o.SyncId, out var to)) o.SyncId = to;
                    foreach (var m in milestones) if (remaps.TryGetValue(m.SyncId, out var to)) m.SyncId = to;
                }

                if (options.Additive)
                {
                    if (options.PrepareAdditions is { } prepare)
                        target = prepare(await SnapshotReader.ReadAsync(db, includeTombstones: true, includeNotified: false, _zone));
                    await ApplyAdditionsAsync(target, options, categories, occasions, milestones, now);
                    await transaction.CommitAsync();
                    db.ChangeTracker.Clear();
                    return;
                }

                // Free the single pin first: the unique index would reject an intermediate state with two pins.
                // Tombstones are outside that index and keep their pin, so undo can restore it.
                foreach (var o in occasions.Where(o => o.IsPinned && o.DeletedAt is null))
                    o.IsPinned = false;
                await db.SaveChangesAsync();

                var categoryBySync = categories.ToDictionary(c => c.SyncId);
                foreach (var dto in target.Categories)
                {
                    if (!categoryBySync.TryGetValue(dto.SyncId, out var category))
                    {
                        category = new Category { SyncId = dto.SyncId };
                        db.Categories.Add(category);
                        categoryBySync[dto.SyncId] = category;
                    }

                    category.Name = dto.Name;
                    category.Emoji = dto.Emoji;
                    category.ColorHex = dto.ColorHex;
                    category.UpdatedAt = dto.UpdatedAt;
                    category.DeletedAt = dto.DeletedAt;
                }

                var targetCategories = target.Categories.Select(c => c.SyncId).ToHashSet();
                foreach (var category in categories.Where(c => c.DeletedAt is null && !targetCategories.Contains(c.SyncId)))
                {
                    category.DeletedAt = now;
                    category.UpdatedAt = now;
                }

                await db.SaveChangesAsync();

                var occasionBySync = occasions.ToDictionary(o => o.SyncId);
                var datesMoved = new HashSet<Guid>();
                foreach (var dto in target.Occasions)
                {
                    if (!occasionBySync.TryGetValue(dto.SyncId, out var occasion))
                    {
                        occasion = new Occasion { SyncId = dto.SyncId };
                        db.Occasions.Add(occasion);
                        occasionBySync[dto.SyncId] = occasion;
                    }

                    occasion.Title = dto.Title;
                    occasion.Emoji = dto.Emoji;
                    occasion.ColorHex = dto.ColorHex;
                    var newDirection = Enum.TryParse<OccasionDirection>(dto.Direction, true, out var direction) ? direction : OccasionDirection.Since;
                    // Compare calendar days: the same day stored at another device's midnight (or read after this device
                    // changed time zone) is not a move, so the instant and the Notified flags stay.
                    var dayMoved = occasion.Id == 0
                        || (occasion.AnchorDay ?? OccasionDates.ToLocalDay(occasion.AnchorDate, _zone)) != dto.AnchorDay;
                    if (occasion.Id != 0 && (dayMoved || occasion.Direction != newDirection))
                        datesMoved.Add(dto.SyncId);
                    if (dayMoved)
                        occasion.AnchorDate = OccasionDates.LocalMidnightUtc(dto.AnchorDay, _zone);
                    occasion.AnchorDay = dto.AnchorDay;
                    occasion.Direction = newDirection;
                    occasion.Notes = dto.Notes;
                    occasion.CreatedAt = dto.CreatedAt == default ? dto.UpdatedAt : dto.CreatedAt;
                    occasion.UpdatedAt = dto.UpdatedAt;
                    occasion.DeletedAt = dto.DeletedAt;
                    occasion.IsPinned = dto.IsPinned;
                    occasion.CategoryId = dto.CategorySyncId is { } categorySyncId
                        && categoryBySync.TryGetValue(categorySyncId, out var category) && category.DeletedAt is null
                            ? category.Id
                            : null;
                }

                var targetOccasions = target.Occasions.Select(o => o.SyncId).ToHashSet();
                foreach (var occasion in occasions.Where(o => o.DeletedAt is null && !targetOccasions.Contains(o.SyncId)))
                {
                    occasion.DeletedAt = now;
                    occasion.UpdatedAt = now;
                    occasion.IsPinned = false;
                }

                await db.SaveChangesAsync();

                var milestoneBySync = milestones.ToDictionary(m => m.SyncId);
                foreach (var dto in target.Milestones)
                {
                    if (!occasionBySync.TryGetValue(dto.OccasionSyncId, out var parent))
                        continue;

                    if (!milestoneBySync.TryGetValue(dto.SyncId, out var milestone))
                    {
                        milestone = new Milestone { SyncId = dto.SyncId };
                        db.Milestones.Add(milestone);
                        milestoneBySync[dto.SyncId] = milestone;
                        // A new milestone that is already reached was announced on the device that has it: do not fire it again here.
                        milestone.Notified = options.TakeNotifiedFromTarget
                            ? dto.Notified
                            : dto.DeletedAt is null && AlreadyReached(parent, dto, now);
                    }
                    else if (options.TakeNotifiedFromTarget)
                    {
                        milestone.Notified = dto.Notified;
                    }
                    else if (datesMoved.Contains(dto.OccasionSyncId) || milestone.ThresholdDays != dto.ThresholdDays)
                    {
                        // Same rule as OccasionService.UpdateAsync: when the date, direction or threshold moves, reached
                        // milestones count as announced and the rest notify when they land.
                        milestone.Notified = dto.DeletedAt is null && AlreadyReached(parent, dto, now);
                    }

                    milestone.OccasionId = parent.Id;
                    milestone.ThresholdDays = dto.ThresholdDays;
                    milestone.Label = dto.Label;
                    milestone.UpdatedAt = dto.UpdatedAt;
                    milestone.DeletedAt = dto.DeletedAt ?? parent.DeletedAt;
                }

                var targetMilestones = target.Milestones.Select(m => m.SyncId).ToHashSet();
                foreach (var milestone in milestones.Where(m => m.DeletedAt is null && !targetMilestones.Contains(m.SyncId)))
                {
                    milestone.DeletedAt = now;
                    milestone.UpdatedAt = now;
                }

                if (options.ApplySettings)
                    await ApplySettingsAsync(target.Settings ?? new AppSettingsDto());

                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                db.ChangeTracker.Clear();
            }
            catch (SnapshotChangedException)
            {
                await transaction.RollbackAsync();
                throw;
            }
            catch
            {
                await transaction.RollbackAsync();
                db.ChangeTracker.Clear();
                throw;
            }
            finally
            {
                db.StampTimestamps = true;
            }
        }

        /// <summary>
        /// The additive restore (<see cref="SnapshotApplyOptions.Additive"/>): adds unknown records, revives local
        /// tombstones with the target's content and leaves live records alone. A live pin never moves, so an added or
        /// revived occasion is stored unpinned while a live one is pinned.
        /// </summary>
        private async Task ApplyAdditionsAsync(ExportModel target, SnapshotApplyOptions options, List<Category> categories,
            List<Occasion> occasions, List<Milestone> milestones, DateTime now)
        {
            var categoryBySync = categories.ToDictionary(c => c.SyncId);
            foreach (var dto in target.Categories.Where(c => c.DeletedAt is null))
            {
                if (categoryBySync.TryGetValue(dto.SyncId, out var category) && category.DeletedAt is null)
                    continue;

                if (category is null)
                {
                    category = new Category { SyncId = dto.SyncId };
                    db.Categories.Add(category);
                    categoryBySync[dto.SyncId] = category;
                }

                category.Name = dto.Name;
                category.Emoji = dto.Emoji;
                category.ColorHex = dto.ColorHex;
                category.UpdatedAt = now;
                category.DeletedAt = null;
            }

            await db.SaveChangesAsync();

            var pinTaken = occasions.Any(o => o.IsPinned && o.DeletedAt is null);
            var occasionBySync = occasions.ToDictionary(o => o.SyncId);
            var applied = new Dictionary<Guid, Occasion>();
            foreach (var dto in target.Occasions.Where(o => o.DeletedAt is null))
            {
                if (occasionBySync.TryGetValue(dto.SyncId, out var occasion) && occasion.DeletedAt is null)
                    continue;

                if (occasion is null)
                {
                    occasion = new Occasion { SyncId = dto.SyncId };
                    db.Occasions.Add(occasion);
                    occasionBySync[dto.SyncId] = occasion;
                }

                occasion.Title = dto.Title;
                occasion.Emoji = dto.Emoji;
                occasion.ColorHex = dto.ColorHex;
                if (occasion.Id == 0 || (occasion.AnchorDay ?? OccasionDates.ToLocalDay(occasion.AnchorDate, _zone)) != dto.AnchorDay)
                    occasion.AnchorDate = OccasionDates.LocalMidnightUtc(dto.AnchorDay, _zone);
                occasion.AnchorDay = dto.AnchorDay;
                occasion.Direction = Enum.TryParse<OccasionDirection>(dto.Direction, true, out var direction) ? direction : OccasionDirection.Since;
                occasion.Notes = dto.Notes;
                occasion.CreatedAt = dto.CreatedAt == default ? dto.UpdatedAt : dto.CreatedAt;
                occasion.UpdatedAt = now;
                occasion.DeletedAt = null;
                occasion.IsPinned = dto.IsPinned && !pinTaken;
                pinTaken |= occasion.IsPinned;
                occasion.CategoryId = dto.CategorySyncId is { } categorySyncId
                    && categoryBySync.TryGetValue(categorySyncId, out var category) && category.DeletedAt is null
                        ? category.Id
                        : null;
                applied[dto.SyncId] = occasion;
            }

            await db.SaveChangesAsync();

            // Milestones come back only with their event: one the user deleted from a live event stays deleted.
            var milestoneBySync = milestones.ToDictionary(m => m.SyncId);
            foreach (var dto in target.Milestones.Where(m => m.DeletedAt is null))
            {
                if (!applied.TryGetValue(dto.OccasionSyncId, out var parent))
                    continue;

                if (milestoneBySync.TryGetValue(dto.SyncId, out var milestone) && milestone.DeletedAt is null)
                    continue;

                if (milestone is null)
                {
                    milestone = new Milestone { SyncId = dto.SyncId };
                    db.Milestones.Add(milestone);
                    milestoneBySync[dto.SyncId] = milestone;
                }

                milestone.Notified = options.TakeNotifiedFromTarget ? dto.Notified : AlreadyReached(parent, dto, now);
                milestone.OccasionId = parent.Id;
                milestone.ThresholdDays = dto.ThresholdDays;
                milestone.Label = dto.Label;
                milestone.UpdatedAt = now;
                milestone.DeletedAt = null;
            }

            if (options.ApplySettings)
                await ApplySettingsAsync(target.Settings ?? new AppSettingsDto());

            await db.SaveChangesAsync();
        }

        public async Task<int> PurgeTombstonesAsync(DateTime olderThanUtc)
        {
            var milestones = await db.Milestones.IgnoreQueryFilters().Where(m => m.DeletedAt < olderThanUtc).ToListAsync();
            var occasions = await db.Occasions.IgnoreQueryFilters().Where(o => o.DeletedAt < olderThanUtc).ToListAsync();
            var categories = await db.Categories.IgnoreQueryFilters().Where(c => c.DeletedAt < olderThanUtc).ToListAsync();

            db.Milestones.RemoveRange(milestones);
            db.Occasions.RemoveRange(occasions);
            db.Categories.RemoveRange(categories);
            await db.SaveChangesAsync();   // EF detaches the deleted entities itself; no tracker reset for the whole session
            return milestones.Count + occasions.Count + categories.Count;
        }

        private bool AlreadyReached(Occasion occasion, MilestoneDto dto, DateTime nowUtc)
        {
            var milestone = new Milestone { ThresholdDays = dto.ThresholdDays };
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), _zone);
            return OccasionDates.IsMilestoneReached(occasion, milestone, OccasionDates.GetDays(occasion, today, _zone));
        }

        private async Task ApplySettingsAsync(AppSettingsDto dto)
        {
            var settings = await db.AppSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AppSettings();
                db.AppSettings.Add(settings);
            }

            settings.Theme = dto.Theme;
            settings.Language = LanguageSetting.IsValid(dto.Language) ? dto.Language : LanguageSetting.System;
            settings.NotificationsEnabled = dto.NotificationsEnabled;
        }
    }
}
