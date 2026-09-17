namespace Markd.Core.Domain
{
    public class AppSettings
    {
        public int Id { get; set; } = 1;

        public string Theme { get; set; } = "System";

        public string Language { get; set; } = "en";

        public bool NotificationsEnabled { get; set; } = true;

        public TimeSpan NotificationTimeOfDay { get; set; } = new(9, 0, 0);

    }
}
