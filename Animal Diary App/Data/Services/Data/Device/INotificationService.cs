namespace Animal_Diary_App.Data.Services.Data.Device
{
    /// <summary>How often a scheduled notification should repeat.</summary>
    public enum NotificationRecurrence
    {
        /// <summary>A single one-shot occurrence. The default, and the only value the
        /// reminder schedulers use: the app expands recurrence rules itself into bounded
        /// concrete occurrences rather than relying on infinite OS recurrence
        /// (AI/design-decisions.md → "Notifications: bounded horizon of concrete instances").</summary>
        Once,
        Daily,
        Weekly
    }

    /// <summary>
    /// Which OS notification channel a message belongs to. Channels are the only
    /// per-category control the user has (Android settings lists one row per channel),
    /// so every distinct kind of reminder gets its own — a carer who wants to silence
    /// the daily nudge must not have to silence medication reminders too.
    /// Ignored on platforms without channels.
    /// </summary>
    public enum NotificationChannelKind
    {
        /// <summary>Medication reminders and missed-dose catch-ups. High importance, makes
        /// a sound, may show as a heads-up.</summary>
        Medication,

        /// <summary>The opt-in once-a-day care reminder. Low importance, never makes a
        /// sound (AI/app-voice.md §8.7).</summary>
        DailyCare
    }

    /// <summary>
    /// A platform-agnostic description of a single local notification. Keeping
    /// this DTO at the device boundary means higher layers (schedulers, view
    /// models) never reference the underlying notification plugin directly.
    /// </summary>
    public class NotificationContent
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime NotifyTime { get; set; }

        /// <summary>Defaults to <see cref="NotificationRecurrence.Once"/> on purpose: an
        /// OS-recurring notification is the one thing the reminder architecture forbids,
        /// so forgetting to set this must not silently produce one.</summary>
        public NotificationRecurrence Recurrence { get; set; } = NotificationRecurrence.Once;

        /// <summary>When true, the notification is posted without sound or vibration
        /// (AI/app-voice.md §8.7: only a user-configured medication reminder may make a
        /// sound). Medication reminders leave this false; the daily care reminder sets it.</summary>
        public bool Silent { get; set; }

        /// <summary>The channel this message belongs to. Defaults to the medication
        /// channel, which is the stricter of the two.</summary>
        public NotificationChannelKind Channel { get; set; } = NotificationChannelKind.Medication;
    }

    /// <summary>
    /// Thin wrapper over the device's local-notification capability. This is the
    /// only type that knows about the notification plugin; everything else talks
    /// to it through this interface.
    /// </summary>
    public interface INotificationService
    {
        Task<bool> RequestNotificationPermissionAsync(bool requestExactAlarm = false);

        /// <summary>
        /// Whether the OS will actually deliver anything we post right now — i.e. the
        /// runtime notification permission is granted AND the user hasn't switched the
        /// app's notifications off in system settings. Both can change at any time
        /// without the app being involved, so this is a live check, never a cached
        /// setup flag.
        /// </summary>
        Task<bool> AreNotificationsEnabledAsync();

        /// <summary>
        /// Register the app's notification channels. Idempotent and safe to call on every
        /// launch (re-registering refreshes the localized channel names). No-op on
        /// platforms without channels.
        /// </summary>
        Task EnsureChannelsAsync();

        /// <summary>
        /// Hand one notification to the OS.
        /// <para><b>Returns false when the OS did not accept it</b> — notifications
        /// disabled, permission missing, or the notify time already stale. Callers must
        /// not record a reminder as armed on a false return: the whole reliability model
        /// depends on the app's idea of what is scheduled matching the OS's.</para>
        /// </summary>
        Task<bool> ScheduleNotification(NotificationContent content);

        Task CancelNotification(int id);
        Task CancelNotifications(IEnumerable<int> ids);

        /// <summary>Cancel every notification this app has scheduled — the "delete
        /// all data" path, where nothing armed with the OS may survive the wipe.</summary>
        Task CancelAllNotifications();
    }
}
