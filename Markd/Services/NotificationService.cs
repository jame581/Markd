using CommunityToolkit.Mvvm.Messaging;
using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.Core.Services;
using Markd.ViewModels;
#if ANDROID || IOS
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using Plugin.LocalNotification.Core.Models.AndroidOption;
using Plugin.LocalNotification.EventArgs;
#endif

namespace Markd.Services
{
    /// <summary>
    /// Milestone alerts. While the app is open, reached milestones surface as the in-app milestone moment.
    /// On phones, upcoming milestones are also scheduled as system notifications at the chosen time of day,
    /// with OPEN and SNOOZE actions, and the schedule is rebuilt whenever occasions change. The desktop has no
    /// system notifications, so there the time of day is when Markd looks for milestones due that day.
    /// </summary>
    public class NotificationService
    {
        public const int OpenActionId = 100;
        public const int SnoozeActionId = 101;
        private const int MaxScheduled = 48;

        private readonly IOccasionService _occasionService;
        private readonly IAppSettingsService _appSettingsService;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private IDispatcherTimer? _clock;
        private DateTime _lastTick = DateTime.Now;
#if ANDROID || IOS
        private CancellationTokenSource? _rescheduleDelay;
#endif

        public event EventHandler<MilestoneReachedEventArgs>? MilestoneReached;

        /// <summary>Raised when a notification (or its OPEN action) asks to show an occasion.</summary>
#pragma warning disable CS0067 // Only phones post notifications with an OPEN action; the desktop never raises it.
        public event EventHandler<int>? OpenOccasionRequested;
#pragma warning restore CS0067

        public NotificationService(IOccasionService occasionService, IAppSettingsService appSettingsService)
        {
            _occasionService = occasionService;
            _appSettingsService = appSettingsService;
#if ANDROID || IOS
            LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationActionTapped;
            WeakReferenceMessenger.Default.Register<NotificationService, OccasionsChangedMessage>(this, (service, message) => service.OnOccasionsChanged(message));
#endif
        }

        /// <summary>
        /// The check made when the app opens or resumes. The desktop waits for the daily check time;
        /// phones check straight away because their notifications already fired at that time.
        /// </summary>
        public async Task CheckOnOpenAsync()
        {
#if WINDOWS
            var settings = await _appSettingsService.GetAsync();
            if (DateTime.Now.TimeOfDay < settings.NotificationTimeOfDay)
                return;
#endif
            await CheckAndNotifyAsync();
        }

        /// <summary>
        /// Watches the clock while the app runs. At local midnight every view reloads its counts; the milestone
        /// check then runs at midnight on phones, or once the daily check time passes on the desktop.
        /// </summary>
        public void StartClock()
        {
            if (_clock is not null || Application.Current is null)
                return;

            _lastTick = DateTime.Now;
            _clock = Application.Current.Dispatcher.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(30);
            _clock.Tick += OnClockTick;
            _clock.Start();
        }

        private async void OnClockTick(object? sender, EventArgs e)
        {
            var previous = _lastTick;
            var now = DateTime.Now;
            _lastTick = now;

            var dayChanged = now.Date != previous.Date;
            if (dayChanged)
                WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));

#if WINDOWS
            var settings = await _appSettingsService.GetAsync();
            var checkAt = now.Date + settings.NotificationTimeOfDay;
            if (previous < checkAt && now >= checkAt)
                await CheckAndNotifyAsync();
#else
            if (dayChanged)
                await CheckAndNotifyAsync();
#endif
        }

        /// <summary>
        /// Marks every reached-but-unannounced milestone as notified and, when alerts are on,
        /// raises <see cref="MilestoneReached"/> for each so the app can show the milestone moment.
        /// </summary>
        public async Task CheckAndNotifyAsync()
        {
            await _gate.WaitAsync();
            try
            {
                var settings = await _appSettingsService.GetAsync();
                var pending = await _occasionService.GetPendingMilestonesAsync();
                if (pending.Count == 0)
                    return;

                foreach (var (occasion, milestone) in pending)
                {
                    await _occasionService.MarkMilestoneNotifiedAsync(milestone.Id);
                    if (!settings.NotificationsEnabled)
                        continue;

                    var days = _occasionService.GetDays(occasion);
                    var next = OccasionMath.GetNextMilestone(occasion, days)?.Milestone;
                    MilestoneReached?.Invoke(this, new MilestoneReachedEventArgs(occasion, milestone, next, days));
                }

                WeakReferenceMessenger.Default.Send(new OccasionsChangedMessage(null, this));
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Rebuilds the schedule of system notifications for upcoming milestones.</summary>
        public async Task RescheduleAsync(bool requestPermission = false)
        {
#if ANDROID || IOS
            try
            {
                var center = LocalNotificationCenter.Current;
                center.CancelAll();

                var settings = await _appSettingsService.GetAsync();
                if (!settings.NotificationsEnabled)
                    return;

                if (requestPermission && !await center.AreNotificationsEnabled())
                    await center.RequestNotificationPermission();

                var now = DateTime.Now;
                var upcoming = (await _occasionService.GetAllAsync())
                    .SelectMany(o => o.Milestones.Where(m => !m.Notified).Select(m => (Occasion: o, Milestone: m)))
                    .Select(x => (x.Occasion, x.Milestone, When: OccasionDates.GetMilestoneDate(x.Occasion, x.Milestone) + settings.NotificationTimeOfDay))
                    .Where(x => x.When > now)
                    .OrderBy(x => x.When)
                    .Take(MaxScheduled);

                foreach (var (occasion, milestone, when) in upcoming)
                    await center.Show(CreateRequest(occasion, milestone, when));
            }
            catch (Exception)
            {
                // Scheduling is best effort; the in-app check still runs on start and resume.
            }
#else
            await Task.CompletedTask;
#endif
        }

#if ANDROID || IOS
        // Deleted, edited, restored or newly added milestones all change the schedule; a burst of changes rebuilds it once.
        private void OnOccasionsChanged(OccasionsChangedMessage message)
        {
            if (ReferenceEquals(message.Source, this))
                return;

            _rescheduleDelay?.Cancel();
            var delay = _rescheduleDelay = new CancellationTokenSource();
            _ = Task.Delay(TimeSpan.FromSeconds(1.5), delay.Token).ContinueWith(
                _ => MainThread.BeginInvokeOnMainThread(async () => await RescheduleAsync()),
                delay.Token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private static NotificationRequest CreateRequest(Occasion occasion, Milestone milestone, DateTime when)
        {
            var since = occasion.Direction == OccasionDirection.Since;
            var culture = LocalizationManager.Instance.Culture;
            var titleKey = since ? "Notification_TitleSince" : "Notification_TitleUntil";
            var titlePattern = Strings.ResourceManager.GetString($"{titleKey}_{Plural.Select(milestone.ThresholdDays, culture)}", culture) ?? titleKey;
            var request = new NotificationRequest
            {
                NotificationId = milestone.Id,
                Title = string.Format(culture, titlePattern, milestone.ThresholdDays, occasion.Emoji).Trim(),
                Description = since
                    ? string.Format(culture, Strings.Notification_DescriptionSince, occasion.Title, milestone.Label)
                    : string.Format(culture, Strings.Notification_DescriptionUntil, occasion.Title, milestone.Label, Plural.Format("Notification_DaysLeft", milestone.ThresholdDays, culture)),
                CategoryType = NotificationCategoryType.Event,
                ReturningData = occasion.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Schedule = new NotificationRequestSchedule { NotifyTime = when }
            };
            request.Schedule.Android.ScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle;
            return request;
        }

        private void OnNotificationActionTapped(NotificationActionEventArgs e)
        {
            if (e.Request is null)
                return;

            if (e.ActionId == SnoozeActionId)
            {
                e.Request.Schedule = new NotificationRequestSchedule { NotifyTime = DateTime.Now.AddHours(1) };
                e.Request.Schedule.Android.ScheduleMode = AndroidScheduleMode.InexactAllowWhileIdle;
                _ = LocalNotificationCenter.Current.Show(e.Request);
                return;
            }

            if ((e.IsTapped || e.ActionId == OpenActionId)
                && int.TryParse(e.Request.ReturningData, out var occasionId))
            {
                MainThread.BeginInvokeOnMainThread(() => OpenOccasionRequested?.Invoke(this, occasionId));
            }
        }
#endif
    }

    public sealed record MilestoneReachedEventArgs(Occasion Occasion, Milestone Milestone, Milestone? NextMilestone, int Days);
}
