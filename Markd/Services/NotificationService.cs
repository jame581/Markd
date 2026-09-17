using Markd.Core.Services;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Markd.Core.Domain;

namespace Markd.Services
{
    public class NotificationService
    {
        private readonly IOccasionService _occasionService;
        private readonly IAppSettingsService _appSettingsService;
        public event EventHandler<MilestoneReachedEventArgs>? MilestoneReached;

        public NotificationService(IOccasionService occasionService, IAppSettingsService appSettingsService)
        {
            _occasionService = occasionService;
            _appSettingsService = appSettingsService;
        }

        /// <summary>
        /// Checks for milestone thresholds that have been reached but not yet notified,
        /// fires a local notification for each, and marks them as notified.
        /// Respects the NotificationsEnabled setting — does nothing when disabled.
        /// </summary>
        public async Task CheckAndNotifyAsync()
        {
            var settings = await _appSettingsService.GetAsync();
            if (!settings.NotificationsEnabled) return;

            var pending = await _occasionService.GetPendingMilestonesAsync();
            if (pending.Count == 0) return;

            foreach (var (occasion, milestone) in pending)
            {
                var nextMilestone = occasion.Milestones
                    .Where(candidate => candidate.Id != milestone.Id && !candidate.Notified)
                    .OrderBy(candidate => candidate.ThresholdDays)
                    .FirstOrDefault();

                var notification = new NotificationRequest
                {
                    NotificationId = milestone.Id,
                    Title = $"🎉 {occasion.Emoji} {occasion.Title}",
                    Description = $"Milestone reached: {milestone.Label} ({milestone.ThresholdDays} days)"
                };

                await LocalNotificationCenter.Current.Show(notification);
                await _occasionService.MarkMilestoneNotifiedAsync(milestone.Id);
                MilestoneReached?.Invoke(this, new MilestoneReachedEventArgs(occasion, milestone, nextMilestone));
            }
        }
    }

    public sealed record MilestoneReachedEventArgs(Occasion Occasion, Milestone Milestone, Milestone? NextMilestone);
}
