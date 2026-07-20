namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Microsoft.Maui.Storage;

/// <summary>
/// Reliable medication-reminder scheduler built for Android background limits.
///
/// Design (see AI/design-decisions.md → "Notifications: bounded horizon"):
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

    // Upper bound of materialized occurrences per medication (14 days × 5
    // times/day = 70 covers the full horizon).
    private const int MaxInstancesPerMedication = 70;

    // Android caps an app at 500 exact alarms; beyond it scheduling silently
    // fails or throws depending on the OS. Keep total pending instances under
    // this budget — meds synced later in a pass get fewer occurrences, and the
    // horizon is re-extended on the next launch/boot anyway.
    private const int GlobalPendingBudget = 400;

    // Persisted marker of the last time the app was confirmed running. Used to
    // tell "the OS already delivered this" from "this fired while we were off".
    private const string LastSeenKey = "reminder_last_seen_ticks";

    // Resolved (fired/missed) instances older than this are pruned to keep the table small.
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(7);

    private readonly INotificationService _notifications;
    private readonly MedicationService _medicationService;
    private readonly PetService _petService;
    private readonly ReminderInstanceService _instances;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly MedicationDoseReconciler _doseReconciler;

    // Serializes every mutation of the instance store + OS schedule. The global
    // catch-up (launch / boot / time-change receivers) and user-triggered syncs
    // can otherwise interleave ClearPending with materialization and duplicate
    // notifications. Public entry points take the gate; *Core methods assume it.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public MedicationReminderScheduler(
        INotificationService notifications,
        MedicationService medicationService,
        PetService petService,
        ReminderInstanceService instances,
        MedicationDoseLogService doseLogService,
        MedicationDoseReconciler doseReconciler)
    {
        _notifications = notifications;
        _medicationService = medicationService;
        _petService = petService;
        _instances = instances;
        _doseLogService = doseLogService;
        _doseReconciler = doseReconciler;
    }

    /// <summary>Ask the OS for exact-alarm permission before scheduling reminders.</summary>
    public Task<bool> RequestPermissionAsync() => _notifications.RequestNotificationPermissionAsync(requestExactAlarm: true);

    // ── Per-medication sync (create / edit / restore) ────────────────────

    /// <summary>
    /// (Re)materialize and schedule all future reminders for one medication from
    /// its saved schedule rules. Idempotent: clears pending instances first, so
    /// it also serves as the "update after edit" path. Archived/missing meds are
    /// cancelled instead.
    /// </summary>
    public async Task SyncMedicationAsync(int medicationId)
    {
        await _gate.WaitAsync();
        try
        {
            await SyncMedicationCoreAsync(medicationId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncMedicationCoreAsync(int medicationId)
    {
        var medication = await _medicationService.GetMedicationByIdAsync(medicationId);
        if (medication == null || medication.IsArchived)
        {
            await CancelMedicationCoreAsync(medicationId);
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
            foreach (var when in MedicationScheduleExpander.Expand(schedule.Day, schedule.Time, now, horizonEnd))
                occurrences.Add((when, slot));
        }

        // Respect the OS exact-alarm budget: this med may only take what's left
        // after every other medication's already-pending occurrences.
        var pendingOthers = (await _instances.GetAllAsync())
            .Count(i => i.Status == ReminderStatus.Pending);
        var budget = Math.Max(0, GlobalPendingBudget - pendingOthers);

        var ordered = occurrences
            .GroupBy(o => o.When)              // dedupe identical day+time across rules
            .Select(g => g.First())
            .OrderBy(o => o.When)
            .Take(Math.Min(MaxInstancesPerMedication, budget))
            .ToList();

        // Persist the whole batch atomically, then arm the one-shots.
        var instances = ordered.Select(o => new ReminderInstance
        {
            MedicationId = medicationId,
            ScheduledTime = o.When,
            SlotIndex = o.Slot,
            Status = ReminderStatus.Pending
        }).ToList();
        await _instances.InsertAllAsync(instances, NotificationIds.ForInstance);

        foreach (var instance in instances)
        {
            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = instance.NotificationId,
                Title = NotificationMessages.MedicationTitle(petName),
                Message = NotificationMessages.MedicationBody(petName, medication.Name, instance.SlotIndex),
                NotifyTime = instance.ScheduledTime,
                Recurrence = NotificationRecurrence.Once
            });
        }
    }

    /// <summary>Cancel and forget every reminder for a medication (archive / delete).</summary>
    public async Task CancelMedicationAsync(int medicationId)
    {
        await _gate.WaitAsync();
        try
        {
            await CancelMedicationCoreAsync(medicationId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CancelMedicationCoreAsync(int medicationId)
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
        await _instances.DeleteAllAsync(pending);
    }

    // ── Global catch-up + re-arm (app launch / device boot) ──────────────

    /// <summary>
    /// Run on every app launch and on device boot. Two jobs:
    ///   1. Resolve every pending occurrence whose time has already passed.
    ///   2. Re-materialize and re-arm all future occurrences (so reminders
    ///      survive reboots, process death, and OEM alarm clearing).
    ///
    /// <paramref name="resendMissed"/> should be <c>true</c> on **device boot**
    /// only. On a reboot, doses scheduled during the off period never fired and
    /// must be re-sent (medication safety). On a normal app launch it is
    /// <c>false</c>: the device was on, so the OS already delivered those
    /// notifications — re-sending would spam duplicates every time the app opens.
    /// </summary>
    public async Task CatchUpAndRefreshAsync(bool resendMissed = true)
    {
        await _gate.WaitAsync();
        try
        {
            await CatchUpAndRefreshCoreAsync(resendMissed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CatchUpAndRefreshCoreAsync(bool resendMissed)
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
            // On boot: anything scheduled within the off window (after the app was
            // last confirmed alive) could not have fired — re-send it. Otherwise
            // assume the OS delivered it and just mark it handled.
            var missed = resendMissed && inst.ScheduledTime > lastSeen;

            // ...unless the carer already logged this dose as taken or skipped —
            // then there's nothing to chase, so suppress the re-send.
            if (missed)
            {
                var doseStatus = await _doseLogService.GetStatusAsync(
                    inst.MedicationId, inst.ScheduledTime.Date, inst.ScheduledTime.TimeOfDay);
                if (doseStatus == DoseStatus.Taken || doseStatus == DoseStatus.Skipped)
                    missed = false;
            }

            if (missed)
                missedToResend.Add(inst);

            inst.Status = missed ? ReminderStatus.Missed : ReminderStatus.Fired;
            await _instances.UpdateAsync(inst);
        }

        await ResendMissedAsync(missedToResend, now);

        // Re-materialize + re-arm every medication; cancel archived ones.
        // (Core variants — the gate is already held.)
        var meds = await _medicationService.GetAllMedicationsAsync();
        foreach (var med in meds)
        {
            if (med.IsArchived)
                await CancelMedicationCoreAsync(med.Id);
            else
                await SyncMedicationCoreAsync(med.Id);
        }

        // Record durable "missed" adherence for past doses never logged.
        await _doseReconciler.ReconcileMissedAsync(now);

        // `all` was mutated in place above (statuses resolved), so it's still an
        // accurate view for pruning — no second full-table scan needed. Instances
        // materialized by the re-arm loop are all Pending and never prunable.
        await PruneHistoryAsync(all, now);
        SetLastSeen(now);
    }

    /// <summary>
    /// Called when the carer marks a dose taken/skipped in the calendar: cancels
    /// that occurrence's still-pending reminder so it can't fire late or be
    /// re-sent by a later boot catch-up.
    /// </summary>
    public async Task MarkDoseHandledAsync(int medicationId, DateTime date, TimeSpan time)
    {
        await _gate.WaitAsync();
        try
        {
            var match = (await _instances.GetByMedicationAsync(medicationId))
                .FirstOrDefault(i => i.Status == ReminderStatus.Pending
                    && i.ScheduledTime.Date == date.Date
                    && i.ScheduledTime.TimeOfDay == time);

            if (match == null)
                return;

            await _notifications.CancelNotification(match.NotificationId);
            match.Status = ReminderStatus.Cancelled;
            await _instances.UpdateAsync(match);
        }
        finally
        {
            _gate.Release();
        }
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

    private async Task PruneHistoryAsync(List<ReminderInstance> all, DateTime now)
    {
        var cutoff = now - HistoryRetention;
        var stale = all
            .Where(i => i.Status != ReminderStatus.Pending && i.ScheduledTime < cutoff)
            .ToList();

        await _instances.DeleteAllAsync(stale);
    }

    private static DateTime GetLastSeen(DateTime fallback)
    {
        var ticks = Preferences.Default.Get(LastSeenKey, 0L);
        return ticks == 0 ? fallback : new DateTime(ticks, DateTimeKind.Local);
    }

    private static void SetLastSeen(DateTime when)
        => Preferences.Default.Set(LastSeenKey, when.Ticks);

    /// <summary>Forget the persisted "last seen" marker. Called by the full data
    /// reset so a fresh start can't inherit the old install's catch-up window.</summary>
    public static void ClearPersistedState()
        => Preferences.Default.Remove(LastSeenKey);
}
