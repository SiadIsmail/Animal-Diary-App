using Android.Content;
using Animal_Diary_App.Data.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Animal_Diary_App;

/// <summary>
/// Shared entry point for the Android broadcast receivers that re-arm medication
/// reminders. Resolves the app's DI services (available even when the UI isn't
/// running) and drives <see cref="MedicationReminderScheduler.CatchUpAndRefreshAsync"/>
/// off the broadcast thread, keeping the receiver alive via <c>goAsync</c>.
/// </summary>
internal static class ReminderRecovery
{
    public static void Run(BroadcastReceiver receiver, bool resendMissed)
    {
        // Keep the receiver alive while we do async work (bounded by the OS).
        var pending = receiver.GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services is null)
                    return;

                var database = services.GetService<AppDatabase>();
                if (database is not null)
                    await database.EnsureInitializedAsync();

                var scheduler = services.GetService<MedicationReminderScheduler>();
                if (scheduler is not null)
                    await scheduler.CatchUpAndRefreshAsync(resendMissed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}
