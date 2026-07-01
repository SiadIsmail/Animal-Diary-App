using Android.App;
using Android.Content;

namespace Animal_Diary_App;

/// <summary>
/// Re-arms medication reminders and re-sends missed doses after the device
/// reboots. Android clears all scheduled alarms on reboot, and any dose whose
/// time fell during the off period would otherwise be lost silently — which is
/// unacceptable for medication. <c>resendMissed: true</c> because a reboot is a
/// genuine device-off gap.
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

        ReminderRecovery.Run(this, resendMissed: true);
    }
}
