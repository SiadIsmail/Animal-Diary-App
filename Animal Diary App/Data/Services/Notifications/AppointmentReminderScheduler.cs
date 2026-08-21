namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Journal;

/// <summary>
/// <b>One</b> notification, the evening before a vet visit.
///
/// <para>One. Not a series, not a same-day nudge, not a follow-up. It states the fact
/// and stops — no urgency, no "don't forget", and nothing about the pet's condition
/// (AI/app-voice.md §8, whose approved patterns include this exact notification).
/// It is silent, on its own channel, because §8.7 reserves sound for medication
/// reminders alone.</para>
///
/// <para><b>Concrete occurrences only, re-armed on launch and boot</b> — the same
/// reliability model as <see cref="MedicationReminderScheduler"/>. Never hand a
/// recurrence rule to the OS (AI/design-decisions.md → "Notifications: bounded horizon
/// of concrete instances"); a visit is a single moment anyway, so the horizon here is
/// simply "the next one per pet".</para>
///
/// <para>The notification id is derived from the visit's local id
/// (<see cref="NotificationIds.Appointment"/>), so moving or deleting a visit cancels
/// exactly its own reminder and nothing else.</para>
/// </summary>
public class AppointmentReminderScheduler
{
    private readonly INotificationService _notifications;
    private readonly VetVisitService _visits;
    private readonly PetService _pets;
    private readonly PetPauseService _pause;

    // Serialize refreshes: the launch/boot pass and an after-save refresh can otherwise
    // interleave and double-arm or double-cancel the same visit's one notification.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppointmentReminderScheduler(
        INotificationService notifications,
        VetVisitService visits,
        PetService pets,
        PetPauseService pause)
    {
        _notifications = notifications;
        _visits = visits;
        _pets = pets;
        _pause = pause;
    }

    /// <summary>
    /// Re-evaluate every pet's next visit and arm or cancel its one reminder. Safe to
    /// call often — launch, boot, and after any visit is added, moved or deleted.
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
            System.Diagnostics.Debug.WriteLine($"[AppointmentReminder] refresh failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drop one visit's reminder outright. Called when a visit is deleted, so
    /// the cancel does not have to wait for the next refresh to notice it is gone —
    /// by then the row is a tombstone and the sweep below can no longer see its id.</summary>
    public async Task CancelAsync(int visitId)
    {
        try
        {
            await _notifications.CancelNotification(NotificationIds.Appointment(visitId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AppointmentReminder] cancel failed: {ex.Message}");
        }
    }

    private async Task RefreshCoreAsync()
    {
        var upcoming = await _visits.GetAllUpcomingAsync();
        if (upcoming.Count == 0)
            return;

        // Every pet once, not one lookup per group. A household with four animals was
        // four round trips into the same table on every launch and every visit save.
        var pets = (await _pets.GetPetsAsync()).ToDictionary(p => p.Id);
        var now = DateTime.Now;

        // Group by pet so each one arms only its NEXT visit. Two appointments in the
        // same fortnight would otherwise both fire, and the second is not news yet.
        foreach (var group in upcoming.GroupBy(v => v.PetId))
        {
            pets.TryGetValue(group.Key, out var pet);
            var next = group.OrderBy(v => v.When).First();

            // Every visit that is not the next one loses its reminder. Cancelling is
            // free and idempotent, and this is what un-arms a visit the owner moved
            // further out.
            foreach (var later in group.Where(v => v.Id != next.Id))
                await _notifications.CancelNotification(NotificationIds.Appointment(later.Id));

            var id = NotificationIds.Appointment(next.Id);
            var fireAt = next.ReminderAt;

            // Paused on this device (§15): this pet's reminders never fire here.
            // Also covers a pet that has vanished from under us.
            if (pet is null || _pause.IsPaused(pet.Id) || fireAt <= now)
            {
                // A fire time already behind us cannot be scheduled — the OS rejects a
                // stale notify time — and there is deliberately no same-day fallback.
                // A visit booked this evening for tomorrow gets no reminder, which is
                // the correct bias: better a missed nudge than one that arrives as the
                // owner is already parking outside the practice.
                await _notifications.CancelNotification(id);
                continue;
            }

            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = id,
                Title = NotificationMessages.AppointmentTitle(),
                Message = NotificationMessages.AppointmentBody(pet.Name, next.Time),
                NotifyTime = fireAt,
                Recurrence = NotificationRecurrence.Once,
                Silent = true,                  // §8.7
                Channel = NotificationChannelKind.Appointment,
            });
        }
    }
}
