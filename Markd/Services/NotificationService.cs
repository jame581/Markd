using Markd.Core.Services;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace Markd.Services
{
    public class NotificationService
    {
        private readonly IOccasionService _occasionService;
        private readonly IAppSettingsService _appSettingsService;

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
                var notification = new NotificationRequest
                {
                    NotificationId = milestone.Id,
                    Title = $"🎉 {occasion.Emoji} {occasion.Title}",
                    Description = $"Milestone reached: {milestone.Label} ({milestone.ThresholdDays} days)"
                };

                await LocalNotificationCenter.Current.Show(notification);
                await _occasionService.MarkMilestoneNotifiedAsync(milestone.Id);
            }
        }
    }
}
