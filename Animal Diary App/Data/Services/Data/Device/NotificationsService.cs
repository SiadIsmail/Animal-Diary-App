namespace Animal_Diary_App.Data.Services.Data.Device;

using Plugin.LocalNotification;
public class NotificationService : INotificationService
{
    public async Task ScheduleDailyNotification(int id,
        string title,
        string message,
        DateTime notifyTime)
    {
        var request = new NotificationRequest
        {
            NotificationId = id,
            Title = title,
            Description = message,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = notifyTime,
                RepeatType = NotificationRepeat.Daily
            }
        };

        await LocalNotificationCenter.Current.Show(request);
    }
    public async Task RequestNotificationPermission()
    {
        var status = await LocalNotificationCenter.Current.RequestNotificationPermission();
        if (!status)
        {
            // Handle permission denial (e.g., show an alert to the user)
        }
    }
}