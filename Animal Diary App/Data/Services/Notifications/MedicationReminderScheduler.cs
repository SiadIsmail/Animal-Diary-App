namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Data.Device;

/// <summary>
/// Domain-level orchestrator for medication reminders. It turns a medication and
/// its reminder times into concrete, warmly-worded notifications and owns their
/// whole lifecycle:
///
///   • <see cref="ScheduleAsync"/> — (re)schedule every reminder for a medication.
///     It cancels first, so calling it again after an edit transparently
///     *updates* the existing notifications.
///   • <see cref="CancelAsync"/> — remove every reminder for a medication
///     (used when a medication is archived or deleted).
///
/// This is the template for future reminder kinds: an AppointmentScheduler or
/// MoodCheckInScheduler would follow the same shape, reusing
/// <see cref="NotificationIds"/> and <see cref="NotificationMessages"/>.
/// </summary>
public class MedicationReminderScheduler
{
    /// <summary>Maximum reminder times the UI lets a user pick per medication.</summary>
    public const int MaxReminderTimes = 5;

    private readonly INotificationService _notifications;

    public MedicationReminderScheduler(INotificationService notifications)
    {
        _notifications = notifications;
    }

    /// <summary>Ask the OS for permission to post notifications (no-op if already granted).</summary>
    public Task RequestPermissionAsync() => _notifications.RequestNotificationPermission();

    /// <summary>
    /// Schedule a daily reminder for each of <paramref name="times"/>. Any
    /// previously scheduled reminders for this medication are cleared first, so
    /// this doubles as the "update after edit" path.
    /// </summary>
    public async Task ScheduleAsync(Medication medication, string petName, IReadOnlyList<TimeSpan> times)
    {
        if (medication is null)
            return;

        // Clear stale reminders first so edits don't leave orphans behind.
        await CancelAsync(medication.Id);

        if (times is null || times.Count == 0)
            return;

        var count = Math.Min(times.Count, MaxReminderTimes);
        for (var slot = 0; slot < count; slot++)
        {
            var content = new NotificationContent
            {
                Id = NotificationIds.MedicationReminder(medication.Id, slot),
                Title = NotificationMessages.MedicationTitle(petName),
                Message = NotificationMessages.MedicationBody(petName, medication.Name, slot),
                NotifyTime = NextOccurrence(times[slot]),
                Recurrence = NotificationRecurrence.Daily
            };

            await _notifications.ScheduleNotification(content);
        }
    }

    /// <summary>Cancel every reminder belonging to a medication.</summary>
    public Task CancelAsync(int medicationId)
        => _notifications.CancelNotifications(NotificationIds.AllMedicationReminders(medicationId));

    /// <summary>The next clock instant for <paramref name="time"/>: today if still ahead, otherwise tomorrow.</summary>
    private static DateTime NextOccurrence(TimeSpan time)
    {
        var next = DateTime.Today.Add(time);
        if (next <= DateTime.Now)
            next = next.AddDays(1);
        return next;
    }
}
