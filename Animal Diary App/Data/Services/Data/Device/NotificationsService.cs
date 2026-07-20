namespace Animal_Diary_App.Data.Services.Data.Device;

using Plugin.LocalNotification;
using System.Diagnostics;

/// <summary>
/// <see cref="INotificationService"/> implementation backed by
/// Plugin.LocalNotification. This is the single point of contact with the
/// plugin so the rest of the app stays plugin-agnostic.
/// </summary>
public class NotificationService : INotificationService
{
    public Task RequestNotificationPermission() => RequestNotificationPermissionAsync();

    public async Task<bool> RequestNotificationPermissionAsync(bool requestExactAlarm = false)
    {
        var permissionRequest = new NotificationPermission
        {
            Android = { RequestPermissionToScheduleExactAlarm = requestExactAlarm }
        };

        var status = await LocalNotificationCenter.Current.RequestNotificationPermission(permissionRequest);
        if (!status)
        {
            Debug.WriteLine("[Notifications] Permission denied for local reminders.");
        }

        return status;
    }

    public async Task ScheduleNotification(NotificationContent content)
    {
        var request = new NotificationRequest
        {
            NotificationId = content.Id,
            Title = content.Title,
            Description = content.Message,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = content.NotifyTime,
                RepeatType = MapRepeat(content.Recurrence)
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }

    public Task CancelNotification(int id)
    {
        LocalNotificationCenter.Current.Cancel(id);
        return Task.CompletedTask;
    }

    public Task CancelNotifications(IEnumerable<int> ids)
    {
        var array = ids?.ToArray() ?? Array.Empty<int>();
        if (array.Length > 0)
            LocalNotificationCenter.Current.Cancel(array);
        return Task.CompletedTask;
    }

    public Task CancelAllNotifications()
    {
        LocalNotificationCenter.Current.CancelAll();
        return Task.CompletedTask;
    }

    private static NotificationRepeat MapRepeat(NotificationRecurrence recurrence) => recurrence switch
    {
        NotificationRecurrence.Daily => NotificationRepeat.Daily,
        NotificationRecurrence.Weekly => NotificationRepeat.Weekly,
        _ => NotificationRepeat.No
    };
}
