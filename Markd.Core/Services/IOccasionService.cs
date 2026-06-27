using Markd.Core.Domain;

namespace Markd.Core.Services
{
    internal interface IOccasionService
    {
        Task<List<Occasion>> GetAllAsync();
        Task<Occasion?> GetByIdAsync(int id);
        Task<Occasion> CreateAsync(Occasion occasion);
        Task<Occasion> UpdateAsync(Occasion occasion);
        Task DeleteAsync(int id);

        /// <summary>
        /// Returns elapsed days for Since, or remaining days for Until.
        /// Negative value means an Until occasion is already past.
        /// </summary>
        int GetDays(Occasion occasion);

        /// <summary>
        /// Checks all Since occasions for hit milestones that haven't been notified yet.
        /// Returns milestones that should trigger a notification.
        /// </summary>
        Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync();
    }
}
