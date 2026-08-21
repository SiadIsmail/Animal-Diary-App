using Android.App;
using Android.Content;

namespace Animal_Diary_App;

/// <summary>
/// Re-arms medication reminders and re-sends missed doses after the device reboots,
/// and re-arms them after the app itself is updated.
///
/// Android clears all scheduled alarms on shutdown, and any dose whose time fell during
/// the off period would otherwise be lost silently, which is unacceptable for
/// medication. A boot is a genuine device-off gap, so it flags a missed-dose re-send.
///
/// <c>MY_PACKAGE_REPLACED</c> is handled for a different reason: a Play Store update
/// installs in the background and stops the app, and the carer may not open Felova for
/// days afterwards. Re-arming immediately means an auto-update can't quietly end the
/// reminders. It is NOT a device-off gap: the update takes seconds and the OS was
/// awake, so it does not request a re-send.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[]
{
    Intent.ActionBootCompleted,
    "android.intent.action.QUICKBOOT_POWERON",
    Intent.ActionMyPackageReplaced
})]
public class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        var action = intent?.Action;

        var isBoot = action == Intent.ActionBootCompleted
                  || action == "android.intent.action.QUICKBOOT_POWERON";
        var isUpdate = action == Intent.ActionMyPackageReplaced;

        if (!isBoot && !isUpdate)
            return;

        ReminderRecovery.Enqueue(this, context, isBootRecovery: isBoot);
    }
}
