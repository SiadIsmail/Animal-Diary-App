using Android.App;
using Android.Content;

namespace Animal_Diary_App;

/// <summary>
/// Re-arms medication reminders when the device's clock or time zone changes.
///
/// Reminder occurrences are stored as local wall-clock times and handed to the OS
/// as absolute alarms computed with the offset known at scheduling time. After a
/// DST transition or a time-zone change, alarms already armed for the current
/// horizon would fire at the wrong wall-clock time until the next re-arm. Handling
/// these system broadcasts re-materializes every reminder against the new local
/// time immediately, instead of waiting for the next app launch or reboot.
///
/// <c>resendMissed: false</c> — a clock change is not a device-off gap, so nothing
/// was actually missed; we only need to re-arm future occurrences.
///
/// TIME_SET / TIMEZONE_CHANGED / DATE_CHANGED are exempt from Android's implicit
/// broadcast restrictions, so a manifest-declared receiver still receives them.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[]
{
    Intent.ActionTimezoneChanged,
    Intent.ActionTimeChanged,
    Intent.ActionDateChanged
})]
public class TimeChangeReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionTimezoneChanged &&
            intent?.Action != Intent.ActionTimeChanged &&
            intent?.Action != Intent.ActionDateChanged)
        {
            return;
        }

        ReminderRecovery.Run(this, resendMissed: false);
    }
}
