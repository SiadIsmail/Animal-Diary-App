namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Journal;
using Microsoft.Maui.Storage;

/// <summary>
/// App-wide, opt-in "daily care reminder" (AI/current-roadmap.md → additional reminder
/// types). Once enabled, each pet gets at most ONE silent notification per day, at the
/// owner's chosen time, and only when something is genuinely still to do that day.
///
/// This is deliberately NOT a re-engagement ping (app-voice.md §8.6 bans those): it is
/// a reminder about owner-configured care tasks, the same category as a medication
/// reminder. If the day is already handled — or the pet is paused on this device — it
/// stays silent. Content comes straight from the same <see cref="PendingItemsService"/>
/// snapshot the Journal chips use, so it can never disagree with what the app shows.
///
/// Reliability model (see <see cref="MedicationReminderScheduler"/>): each occurrence is
/// a one-shot armed for TODAY only, and re-evaluated on every launch/resume and after
/// every logging write. We only ever arm today's still-future occurrence — never a
/// future day whose state we haven't checked — so the app can't fire a reminder into a
/// day the owner already handled. The cost is that a day with no app interaction before
/// the chosen time gets no reminder, which is the correct bias: better a missed nudge
/// than a wrong one (§8, §11).
/// </summary>
public class DailyCareReminderScheduler
{
    private readonly INotificationService _notifications;
    private readonly PetService _petService;
    private readonly PetPauseService _pause;
    private readonly PendingItemsService _pending;

    // Serialize refreshes: launch/boot catch-up and after-write refreshes can otherwise
    // interleave and double-arm or double-cancel the same pet's one notification.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DailyCareReminderScheduler(
        INotificationService notifications,
        PetService petService,
        PetPauseService pause,
        PendingItemsService pending)
    {
        _notifications = notifications;
        _petService = petService;
        _pause = pause;
        _pending = pending;
    }

    /// <summary>
    /// Re-evaluate today's daily reminder for every pet and arm or cancel each one.
    /// Safe to call often (launch, resume, after every logging write, on pause/resume).
    /// A quiet no-op when the feature is off, offline, or nothing is pending.
    /// </summary>
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DailyCareReminder] refresh failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshCoreAsync()
    {
        var pets = await _petService.GetPetsAsync();

        // Feature off: make sure nothing stays armed from when it was on.
        if (!DailyCareReminderSettings.Enabled)
        {
            foreach (var pet in pets)
                await _notifications.CancelNotification(NotificationIds.DailyCare(pet.Id));
            return;
        }

        var now = DateTime.Now;
        var fireToday = now.Date + DailyCareReminderSettings.Time;

        foreach (var pet in pets)
        {
            var id = NotificationIds.DailyCare(pet.Id);

            // Paused on this device (§15): never fires for this pet.
            if (_pause.IsPaused(pet.Id))
            {
                await _notifications.CancelNotification(id);
                continue;
            }

            // Only arm while today's chosen time is still ahead of us. A time already
            // past today can't be scheduled (and if it was armed earlier, it already
            // fired) — the next launch before tomorrow's time re-arms tomorrow's.
            if (fireToday <= now)
            {
                await _notifications.CancelNotification(id);
                continue;
            }

            // Nothing left to do today → stay silent, and drop any reminder armed
            // earlier today before the owner finished logging.
            var care = await _pending.GetTodayCareAsync(pet, now);
            if (care.Pending.Count == 0)
            {
                await _notifications.CancelNotification(id);
                continue;
            }

            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = id,
                Title = NotificationMessages.DailyCareTitle(pet.Name),
                Message = NotificationMessages.DailyCareBody(pet.Name),
                NotifyTime = fireToday,
                Recurrence = NotificationRecurrence.Once,
                Silent = true, // §8.7: only medication reminders may make a sound.
            });
        }
    }
}

/// <summary>
/// Per-device settings for the daily care reminder, stored in <see cref="Preferences"/>
/// (device-local, like the medication catch-up marker and pause state — never synced,
/// cleared by the full data reset). Off by default; the owner turns it on and picks a
/// time in Settings.
/// </summary>
public static class DailyCareReminderSettings
{
    private const string EnabledKey = "daily_care_reminder_enabled";
    private const string TimeKey = "daily_care_reminder_time_ticks";

    // A calm evening default for when the owner enables it without picking a time.
    private static readonly TimeSpan DefaultTime = new(20, 0, 0);

    public static bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, false);
        set => Preferences.Default.Set(EnabledKey, value);
    }

    public static TimeSpan Time
    {
        get
        {
            var ticks = Preferences.Default.Get(TimeKey, DefaultTime.Ticks);
            var t = new TimeSpan(ticks);
            return t < TimeSpan.Zero || t >= TimeSpan.FromDays(1) ? DefaultTime : t;
        }
        set => Preferences.Default.Set(TimeKey, value.Ticks);
    }

    /// <summary>Forget both settings. Called by the full data reset.</summary>
    public static void ClearPersistedState()
    {
        Preferences.Default.Remove(EnabledKey);
        Preferences.Default.Remove(TimeKey);
    }
}
