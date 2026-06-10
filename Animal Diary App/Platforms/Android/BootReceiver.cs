using Android.App;
using Android.Content;
using Animal_Diary_App.Data.Services.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Animal_Diary_App;

/// <summary>
/// Re-arms medication reminders and re-sends missed doses after the device
/// reboots. Android clears all scheduled alarms on reboot, and any dose whose
/// time fell during the off period would otherwise be lost silently — which is
/// unacceptable for medication. On boot we resolve the app's services and run
/// <see cref="MedicationReminderScheduler.CatchUpAndRefreshAsync"/>.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted, "android.intent.action.QUICKBOOT_POWERON" })]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted &&
            intent?.Action != "android.intent.action.QUICKBOOT_POWERON")
        {
            return;
        }

        // Keep the receiver alive while we do async work (bounded by the OS).
        var pending = GoAsync();
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
                    await scheduler.CatchUpAndRefreshAsync();
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
