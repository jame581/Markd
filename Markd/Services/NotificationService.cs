using Markd.Core.Services;
using Plugin.LocalNotification;

namespace Markd.Services
{
    public class NotificationService
    {
        private readonly IOccasionService _occasionService;

        public NotificationService(IOccasionService occasionService)
        {
            _occasionService = occasionService;
        }

        /// <summary>
        /// Checks for milestone thresholds that have been reached but not yet notified,
        /// fires a local notification for each, and marks them as notified.
        /// </summary>
        public async Task CheckAndNotifyAsync()
        {
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
