namespace Animal_Diary_App.Data.Services.Data.Device
{
    public interface INotificationService
    {
        Task ScheduleDailyNotification(int id, string title, string message, DateTime notifyTime);
        Task RequestNotificationPermission();
    }
}