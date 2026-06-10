namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Microsoft.Maui.Storage;

/// <summary>
/// Reliable medication-reminder scheduler built for Android background limits.
///
/// Design (see AI_CONTEXT.md §6):
///   • Recurrence rules ("Mon &amp; Thu at 09:00") live only in the data layer
///     (<see cref="MedicationSchedule"/>).
///   • At runtime those rules are *expanded* into independent, single-shot
///     <see cref="ReminderInstance"/> occurrences over a bounded horizon — never
///     an infinite OS recurrence (which Android throttles/drops).
///   • Each occurrence is scheduled as a one-shot exact notification.
///   • On every app launch and device boot we run <see cref="CatchUpAndRefreshAsync"/>,
///     which re-arms future occurrences AND re-sends doses that were missed while
///     the device was off — critical for medication.
/// </summary>
public class MedicationReminderScheduler
{
    /// <summary>Maximum reminder times the UI lets a user pick per medication.</summary>
    public const int MaxReminderTimes = 5;

    // How far ahead we pre-schedule concrete notifications. Re-extended on every
    // app launch and device boot, so it only needs to cover a typical gap
    // between app opens, while staying well under the OS exact-alarm budget.
    private const int HorizonDays = 14;

    // A dose whose trigger passed while the device was off is re-sent as a
    // "missed dose" catch-up rather than silently dropped.
    private const int MaxInstancesPerMedication = 70;

    // Persisted marker of the last time the app was confirmed running. Used to
    // tell "the OS already delivered this" from "this fired while we were off".
    private const string LastSeenKey = "reminder_last_seen_ticks";

    // Resolved (fired/missed) instances older than this are pruned to keep the table small.
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(7);

    private readonly INotificationService _notifications;
    private readonly MedicationService _medicationService;
    private readonly PetService _petService;
    private readonly ReminderInstanceService _instances;

    public MedicationReminderScheduler(
        INotificationService notifications,
        MedicationService medicationService,
        PetService petService,
        ReminderInstanceService instances)
    {
        _notifications = notifications;
        _medicationService = medicationService;
        _petService = petService;
        _instances = instances;
    }

    /// <summary>Ask the OS for permission to post notifications (no-op if already granted).</summary>
    public Task RequestPermissionAsync() => _notifications.RequestNotificationPermission();

    // ── Per-medication sync (create / edit / restore) ────────────────────

    /// <summary>
    /// (Re)materialize and schedule all future reminders for one medication from
    /// its saved schedule rules. Idempotent: clears pending instances first, so
    /// it also serves as the "update after edit" path. Archived/missing meds are
    /// cancelled instead.
    /// </summary>
    public async Task SyncMedicationAsync(int medicationId)
    {
        var medication = await _medicationService.GetMedicationByIdAsync(medicationId);
        if (medication == null || medication.IsArchived)
        {
            await CancelMedicationAsync(medicationId);
            return;
        }

        // Clean slate for future occurrences; fired/missed history is preserved.
        await ClearPendingAsync(medicationId);

        var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(medicationId);
        if (schedules.Count == 0)
            return;

        var pet = await _petService.GetPetByIdAsync(medication.PetId);
        var petName = pet?.Name ?? string.Empty;

        // Stable slot index per distinct time, for message rotation.
        var distinctTimes = schedules.Select(s => s.Time).Distinct().OrderBy(t => t).ToList();

        var now = DateTime.Now;
        var horizonEnd = now.AddDays(HorizonDays);

        // Expand every (weekday + time) rule into concrete occurrences.
        var occurrences = new List<(DateTime When, int Slot)>();
        foreach (var schedule in schedules)
        {
            var slot = distinctTimes.IndexOf(schedule.Time);
            foreach (var when in ExpandOccurrences(schedule.Day, schedule.Time, now, horizonEnd))
                occurrences.Add((when, slot));
        }

        var ordered = occurrences
            .GroupBy(o => o.When)              // dedupe identical day+time across rules
            .Select(g => g.First())
            .OrderBy(o => o.When)
            .Take(MaxInstancesPerMedication)
            .ToList();

        foreach (var (when, slot) in ordered)
        {
            var instance = new ReminderInstance
            {
                MedicationId = medicationId,
                ScheduledTime = when,
                SlotIndex = slot,
                Status = ReminderStatus.Pending
            };
            await _instances.InsertAsync(instance);               // assigns Id
            instance.NotificationId = NotificationIds.ForInstance(instance.Id);
            await _instances.UpdateAsync(instance);

            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = instance.NotificationId,
                Title = NotificationMessages.MedicationTitle(petName),
                Message = NotificationMessages.MedicationBody(petName, medication.Name, slot),
                NotifyTime = when,
                Recurrence = NotificationRecurrence.Once
            });
        }
    }

    /// <summary>Cancel and forget every reminder for a medication (archive / delete).</summary>
    public async Task CancelMedicationAsync(int medicationId)
    {
        var all = await _instances.GetByMedicationAsync(medicationId);
        await _notifications.CancelNotifications(all.Select(i => i.NotificationId));
        // Also clear any notifications left over from the legacy daily-repeat scheme.
        await _notifications.CancelNotifications(NotificationIds.AllMedicationReminders(medicationId));
        await _instances.DeleteAllByMedicationAsync(medicationId);
    }

    private async Task ClearPendingAsync(int medicationId)
    {
        var pending = (await _instances.GetByMedicationAsync(medicationId))
            .Where(i => i.Status == ReminderStatus.Pending)
            .ToList();

        await _notifications.CancelNotifications(pending.Select(i => i.NotificationId));
        foreach (var inst in pending)
            await _instances.DeleteAsync(inst);
    }

    // ── Global catch-up + re-arm (app launch / device boot) ──────────────

    /// <summary>
    /// Run on every app launch and on device boot. Two jobs:
    ///   1. Resolve every pending occurrence whose time has already passed —
    ///      re-sending any that fired while the device was off (missed doses).
    ///   2. Re-materialize and re-arm all future occurrences (so reminders
    ///      survive reboots, process death, and OEM alarm clearing).
    /// </summary>
    public async Task CatchUpAndRefreshAsync()
    {
        var now = DateTime.Now;
        var lastSeen = GetLastSeen(now);

        var all = await _instances.GetAllAsync();

        var pendingPast = all
            .Where(i => i.Status == ReminderStatus.Pending && i.ScheduledTime <= now)
            .OrderBy(i => i.ScheduledTime)
            .ToList();

        var missedToResend = new List<ReminderInstance>();
        foreach (var inst in pendingPast)
        {
            // If the trigger fell after the app was last confirmed alive, the OS
            // almost certainly could not deliver it (device off / rebooting), so
            // we must re-send it. Otherwise assume the OS delivered it and just
            // mark it handled to avoid a duplicate.
            if (inst.ScheduledTime > lastSeen)
                missedToResend.Add(inst);

            inst.Status = inst.ScheduledTime > lastSeen ? ReminderStatus.Missed : ReminderStatus.Fired;
            await _instances.UpdateAsync(inst);
        }

        await ResendMissedAsync(missedToResend, now);

        // Re-materialize + re-arm every medication; cancel archived ones.
        var meds = await _medicationService.GetAllMedicationsAsync();
        foreach (var med in meds)
        {
            if (med.IsArchived)
                await CancelMedicationAsync(med.Id);
            else
                await SyncMedicationAsync(med.Id);
        }

        await PruneHistoryAsync(now);
        SetLastSeen(now);
    }

    private async Task ResendMissedAsync(List<ReminderInstance> missed, DateTime now)
    {
        if (missed.Count == 0)
            return;

        // One clear catch-up per medication, rather than a flood of stale alerts.
        foreach (var group in missed.GroupBy(m => m.MedicationId))
        {
            var med = await _medicationService.GetMedicationByIdAsync(group.Key);
            if (med == null || med.IsArchived)
                continue;

            var pet = await _petService.GetPetByIdAsync(med.PetId);
            var petName = pet?.Name ?? string.Empty;

            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = NotificationIds.MissedDose(med.Id),
                Title = NotificationMessages.MedicationMissedTitle(petName),
                Message = NotificationMessages.MedicationMissedBody(petName, med.Name, group.Count()),
                NotifyTime = now.AddSeconds(2),     // deliver right away
                Recurrence = NotificationRecurrence.Once
            });
        }
    }

    private async Task PruneHistoryAsync(DateTime now)
    {
        var cutoff = now - HistoryRetention;
        var stale = (await _instances.GetAllAsync())
            .Where(i => i.Status != ReminderStatus.Pending && i.ScheduledTime < cutoff)
            .ToList();

        foreach (var inst in stale)
            await _instances.DeleteAsync(inst);
    }

    /// <summary>
    /// All occurrences of <paramref name="day"/> at <paramref name="time"/> in
    /// the window (<paramref name="from"/>, <paramref name="until"/>], computed in
    /// local wall-clock time.
    /// </summary>
    private static IEnumerable<DateTime> ExpandOccurrences(DayOfWeek day, TimeSpan time, DateTime from, DateTime until)
    {
        var daysUntil = ((int)day - (int)from.DayOfWeek + 7) % 7;
        var candidate = from.Date.AddDays(daysUntil).Add(time);
        if (candidate <= from)
            candidate = candidate.AddDays(7);

        while (candidate <= until)
        {
            yield return candidate;
            candidate = candidate.AddDays(7);
        }
    }

    private static DateTime GetLastSeen(DateTime fallback)
    {
        var ticks = Preferences.Default.Get(LastSeenKey, 0L);
        return ticks == 0 ? fallback : new DateTime(ticks, DateTimeKind.Local);
    }

    private static void SetLastSeen(DateTime when)
        => Preferences.Default.Set(LastSeenKey, when.Ticks);
}
