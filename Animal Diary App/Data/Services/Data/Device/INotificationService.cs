namespace Animal_Diary_App.Data.Services.Data.Device
{
    /// <summary>How often a scheduled notification should repeat.</summary>
    public enum NotificationRecurrence
    {
        Once,
        Daily,
        Weekly
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
        public NotificationRecurrence Recurrence { get; set; } = NotificationRecurrence.Daily;
    }

    /// <summary>
    /// Thin wrapper over the device's local-notification capability. This is the
    /// only type that knows about the notification plugin; everything else talks
    /// to it through this interface.
    /// </summary>
    public interface INotificationService
    {
        Task RequestNotificationPermission();
        Task<bool> RequestNotificationPermissionAsync(bool requestExactAlarm = false);
        Task ScheduleNotification(NotificationContent content);
        Task CancelNotification(int id);
        Task CancelNotifications(IEnumerable<int> ids);

        /// <summary>Cancel every notification this app has scheduled — the "delete
        /// all data" path, where nothing armed with the OS may survive the wipe.</summary>
        Task CancelAllNotifications();
    }
}
