using Markd.Core.Domain;

namespace Markd.Core.Services
{
    public interface IOccasionService
    {
        Task<List<Occasion>> GetAllAsync();
        Task<Occasion?> GetByIdAsync(int id);
        Task<Occasion> CreateAsync(Occasion occasion);
        Task<Occasion> UpdateAsync(Occasion occasion);
        Task DeleteAsync(int id);

        /// <summary>
        /// Re-inserts a previously deleted occasion (as a new record) together with its milestones.
        /// Used by undo; milestone Notified flags are preserved so alerts do not repeat.
        /// </summary>
        Task<Occasion> RestoreAsync(Occasion snapshot);
        Task DeleteAllAsync();

        /// <summary>
        /// Hard-deletes every occasion, milestone and category, tombstones included, so nothing is left for a sync to
        /// read as a delete or to set against the other devices' edits (a kept category renamed elsewhere would be a
        /// conflict on reconnect). Settings stay. Only for erasing a device that has sync state, and only after
        /// <see cref="Sync.ISyncService.ForgetLinkAsync"/> returned <see cref="Sync.SyncOutcome.Disconnected"/>: forget
        /// the link FIRST, then purge, so a crash between the two leaves local data intact and unlinked.
        /// </summary>
        Task PurgeAllAsync();
        Task SetPinnedAsync(int id);
        Task<Milestone> AddMilestoneAsync(int occasionId, int thresholdDays, string label);
        Task RemoveMilestoneAsync(int milestoneId);
        Task<IReadOnlyList<CalendarMark>> GetCalendarMarksAsync(int year, int month);

        /// <summary>
        /// Returns elapsed days for Since, or remaining days for Until.
        /// Negative value means an Until occasion is already past.
        /// </summary>
        int GetDays(Occasion occasion);

        /// <summary>
        /// Checks all occasions for reached milestones that haven't been notified yet
        /// (Since: days elapsed ≥ threshold; Until: days remaining ≤ threshold).
        /// Returns milestones that should trigger a notification.
        /// </summary>
        Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync();

        /// <summary>
        /// Marks a milestone as notified so it won't be returned again by GetPendingMilestonesAsync.
        /// </summary>
        Task MarkMilestoneNotifiedAsync(int milestoneId);
    }
}
