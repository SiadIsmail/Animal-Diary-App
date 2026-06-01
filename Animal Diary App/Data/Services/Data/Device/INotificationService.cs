namespace Animal_Diary_App.Data.Services.Data.Device
{
    public interface INotificationService
    {
        Task ScheduleNotification(string title, string message, DateTime notifyTime);
    }
}