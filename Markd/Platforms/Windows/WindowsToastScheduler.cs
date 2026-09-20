using System.Globalization;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Services;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Markd.Platforms.Windows
{
    /// <summary>
    /// Hands upcoming milestones to Windows as scheduled toasts, so they arrive with the app closed.
    /// Every WinRT call lives here; the rule about what to schedule is in <see cref="MilestoneSchedule"/>.
    /// </summary>
    internal static class WindowsToastScheduler
    {
        private const string Group = "markd.milestones";

        private static bool? _available;

        /// <summary>
        /// Scheduled toasts need package identity. The Store MSIX has it, the release zip does not,
        /// and the same binary ships both ways, so this is a runtime question rather than a compile-time one.
        /// </summary>
        public static bool IsAvailable => _available ??= CheckAvailable();

        private static bool CheckAvailable()
        {
            try
            {
                _ = global::Windows.ApplicationModel.Package.Current.Id;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public static void Clear()
        {
            if (!IsAvailable)
                return;

            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var scheduled in notifier.GetScheduledToastNotifications())
            {
                if (scheduled.Group == Group)
                    notifier.RemoveFromSchedule(scheduled);
            }
        }

        public static void Schedule(IReadOnlyList<ScheduledMilestone> upcoming, CultureInfo culture)
        {
            if (!IsAvailable)
                return;

            var notifier = ToastNotificationManager.CreateToastNotifier();
            foreach (var entry in upcoming)
            {
                var toast = new ScheduledToastNotification(Build(entry, culture), entry.When)
                {
                    Group = Group,
                    Tag = entry.Milestone.Id.ToString(CultureInfo.InvariantCulture)
                };
                notifier.AddToSchedule(toast);
            }
        }

        private static XmlDocument Build(ScheduledMilestone entry, CultureInfo culture)
        {
            var (occasion, milestone, _) = entry;
            var since = occasion.Direction == OccasionDirection.Since;

            var titleKey = since ? "Notification_TitleSince" : "Notification_TitleUntil";
            var titlePattern = Strings.ResourceManager.GetString(
                $"{titleKey}_{Plural.Select(milestone.ThresholdDays, culture)}", culture) ?? titleKey;
            var title = string.Format(culture, titlePattern, milestone.ThresholdDays, occasion.Emoji).Trim();

            var body = since
                ? string.Format(culture, Strings.Notification_DescriptionSince, occasion.Title, milestone.Label)
                : string.Format(culture, Strings.Notification_DescriptionUntil, occasion.Title, milestone.Label,
                    Plural.Format("Notification_DaysLeft", milestone.ThresholdDays, culture));

            var xml = new XmlDocument();
            xml.LoadXml($"""
                <toast launch="{Escape(occasion.Id.ToString(CultureInfo.InvariantCulture))}">
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{Escape(title)}</text>
                      <text>{Escape(body)}</text>
                    </binding>
                  </visual>
                </toast>
                """);
            return xml;
        }

        // Titles and notes are user text and routinely contain & or <, which would break the toast XML.
        private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    }
}
