namespace Animal_Diary_App.Data.Services.Data.Device;

using Animal_Diary_App.Helpers;
using Plugin.LocalNotification;
using System.Diagnostics;
#if ANDROID
using Plugin.LocalNotification.AndroidOption;
#endif

/// <summary>
/// <see cref="INotificationService"/> implementation backed by
/// Plugin.LocalNotification. This is the single point of contact with the
/// plugin so the rest of the app stays plugin-agnostic.
/// </summary>
public class NotificationService : INotificationService
{
    // Android channel ids. These are BAKED INTO EXISTING INSTALLS once created: a
    // channel's importance is immutable after registration, so changing the importance
    // of an already-shipped channel requires a NEW id (the old one lingers in system
    // settings until the app is reinstalled). Version the id if that ever becomes
    // necessary; never "fix" importance in place and expect it to take effect.
    private const string MedicationChannelId = "felova.medication.v1";
    private const string DailyCareChannelId = "felova.dailycare.v1";
    private const string AppointmentChannelId = "felova.appointment.v1";

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

    public async Task<bool> AreNotificationsEnabledAsync()
    {
        try
        {
            // requestExactAlarm is deliberately absent: exact-alarm capability is not
            // part of "can we deliver a reminder" for this app (AI/design-decisions.md
            // → "Reminders use inexact (allow-while-idle) alarms"). Passing a permission
            // object that asks for it would make this report false on every modern device.
            return await LocalNotificationCenter.Current.AreNotificationsEnabled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notifications] enabled-check failed: {ex.Message}");
            return true;    // never block scheduling on a failed probe
        }
    }

    // Guards the lazy registration below. Channel importance is immutable once the
    // channel exists, so "registered late" is not a recoverable state.
    private bool _channelsRegistered;

    public Task EnsureChannelsAsync()
    {
        // Set even if registration throws, and on platforms without channels: the
        // per-schedule guard must not turn into a retry on every notification.
        _channelsRegistered = true;

#if ANDROID
        try
        {
            var L = LocalizationManager.Instance;

            // Registering an existing channel again is a no-op for importance but does
            // refresh name/description, so this keeps the labels in the user's language
            // after a language switch.
            LocalNotificationCenter.CreateNotificationChannels(new List<NotificationChannelRequest>
            {
                new()
                {
                    Id = MedicationChannelId,
                    Name = L.GetString("Notif_ChannelMedicationName"),
                    Description = L.GetString("Notif_ChannelMedicationDescription"),
                    // High so a dose reminder can surface as a heads-up rather than a
                    // silent tray entry the carer finds hours later.
                    Importance = AndroidImportance.High,
                    EnableSound = true,
                    EnableVibration = true,
                },
                new()
                {
                    Id = DailyCareChannelId,
                    Name = L.GetString("Notif_ChannelDailyCareName"),
                    Description = L.GetString("Notif_ChannelDailyCareDescription"),
                    // Low: appears in the tray, never interrupts (§8.7).
                    Importance = AndroidImportance.Low,
                    EnableSound = false,
                    EnableVibration = false,
                },
                new()
                {
                    Id = AppointmentChannelId,
                    Name = L.GetString("Notif_ChannelAppointmentName"),
                    Description = L.GetString("Notif_ChannelAppointmentDescription"),
                    // Low and silent like the daily nudge, but separately controllable:
                    // silencing one must not silence the other.
                    Importance = AndroidImportance.Low,
                    EnableSound = false,
                    EnableVibration = false,
                },
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notifications] channel registration failed: {ex.Message}");
        }
#endif
        return Task.CompletedTask;
    }

    public async Task<bool> ScheduleNotification(NotificationContent content)
    {
        // Register before the first post, whatever the call order. The plugin creates
        // any channel id it doesn't recognise itself: at DEFAULT importance, with its
        // own generic name, and a channel's importance can never be raised afterwards.
        // Losing that race once would permanently demote medication reminders on that
        // install, so this must not depend on startup having got there first.
        if (!_channelsRegistered)
            await EnsureChannelsAsync();

        var request = new NotificationRequest
        {
            NotificationId = content.Id,
            Title = content.Title,
            Description = content.Message,
            // §8.7: the daily care reminder is silent; medication reminders keep their sound.
            Silent = content.Silent,
            // Lets a carer's Do Not Disturb rules pass reminders through while still
            // blocking everything else.
            CategoryType = NotificationCategoryType.Reminder,
            Schedule = new NotificationRequestSchedule
            {
                NotifyTime = content.NotifyTime,
                RepeatType = MapRepeat(content.Recurrence)
            }
        };

#if ANDROID
        request.Android.ChannelId = content.Channel switch
        {
            NotificationChannelKind.DailyCare => DailyCareChannelId,
            NotificationChannelKind.Appointment => AppointmentChannelId,
            _ => MedicationChannelId,
        };
#endif

        try
        {
            // The plugin returns false WITHOUT scheduling when notifications are
            // disabled or the notify time is already stale. Propagating it is what lets
            // the scheduler avoid recording a reminder the OS never accepted.
            var accepted = await LocalNotificationCenter.Current.Show(request);
            if (!accepted)
                Debug.WriteLine($"[Notifications] OS rejected notification {content.Id} (disabled or stale notify time).");

            return accepted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Notifications] scheduling {content.Id} failed: {ex.Message}");
            return false;
        }
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
