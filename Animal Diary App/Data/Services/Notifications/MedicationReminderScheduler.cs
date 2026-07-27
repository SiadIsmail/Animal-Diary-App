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
///   • Each occurrence is scheduled as a one-shot notification.
///   • On every app launch and device boot we run <see cref="CatchUpAndRefreshAsync"/>,
///     which re-arms future occurrences AND re-sends doses that were missed while
///     the device was off — critical for medication.
///
/// Invariant this class exists to maintain: <b>a stored Pending instance means the OS
/// actually accepted that notification.</b> Anything else makes the catch-up lie — it
/// would resolve a never-armed occurrence to <see cref="ReminderStatus.Fired"/> and
/// suppress the boot re-send that is the app's last line of defence.
/// </summary>
public class MedicationReminderScheduler
{
    /// <summary>Maximum reminder times the UI lets a user pick per medication.</summary>
    public const int MaxReminderTimes = 5;

#if IOS || MACCATALYST
    // iOS/macOS hard-cap an app at 64 pending UNNotificationRequests and silently keep
    // only the soonest-firing ones beyond that. Budget below the cap, leaving headroom
    // for the per-pet daily-care and per-medication missed-dose ids, which are armed
    // outside this budget. There is also no boot receiver on these platforms, so the
    // horizon is only ever re-extended on launch.
    private const int HorizonDays = 7;
    private const int MaxInstancesPerMedication = 30;
    private const int GlobalPendingBudget = 56;
#else
    // How far ahead we pre-schedule concrete notifications. Re-extended on every
    // app launch and device boot, so it only needs to cover a typical gap
    // between app opens, while staying well under the OS alarm budget.
    private const int HorizonDays = 14;

    // Upper bound of materialized occurrences per medication (14 days × 5
    // times/day = 70 covers the full horizon).
    private const int MaxInstancesPerMedication = 70;

    // Android caps an app at ~500 scheduled alarms; beyond it scheduling silently
    // fails or throws depending on the OS. Keep total pending instances under
    // this budget — meds synced later in a pass get fewer occurrences, and the
    // horizon is re-extended on the next launch/boot anyway.
    private const int GlobalPendingBudget = 400;
#endif

    // Persisted marker of the last time the app was confirmed alive. Used to tell
    // "the OS already delivered this" from "this fired while we were off".
    private const string LastSeenKey = "reminder_last_seen_ticks";

    // Set by the Android boot receiver the instant a BOOT_COMPLETED arrives, and
    // cleared only once a catch-up has actually done the missed-dose re-send. See
    // CatchUpAndRefreshCoreAsync for why this exists.
    private const string BootRecoveryPendingKey = "reminder_boot_recovery_pending";

    // One-time cleanup of notifications left by the pre-instance daily-repeat scheme.
    private const string LegacyIdsPurgedKey = "reminder_legacy_ids_purged";

    // Resolved (fired/missed) instances older than this are pruned to keep the table small.
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromDays(7);

    private readonly INotificationService _notifications;
    private readonly MedicationService _medicationService;
    private readonly PetService _petService;
    private readonly ReminderInstanceService _instances;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly MedicationDoseReconciler _doseReconciler;
    private readonly PetPauseService _pause;

    // Serializes every mutation of the instance store + OS schedule. The global
    // catch-up (launch / boot / time-change receivers) and user-triggered syncs
    // can otherwise interleave ClearPending with materialization and duplicate
    // notifications. Public entry points take the gate; *Core methods assume it.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Short-lived cache of "will the OS deliver anything right now". Only ever read
    // and written under _gate, so it needs no synchronization of its own.
    private bool? _deliveryEnabled;
    private DateTime _deliveryEnabledAt;

    public MedicationReminderScheduler(
        INotificationService notifications,
        MedicationService medicationService,
        PetService petService,
        ReminderInstanceService instances,
        MedicationDoseLogService doseLogService,
        MedicationDoseReconciler doseReconciler,
        PetPauseService pause)
    {
        _notifications = notifications;
        _medicationService = medicationService;
        _petService = petService;
        _instances = instances;
        _doseLogService = doseLogService;
        _doseReconciler = doseReconciler;
        _pause = pause;
    }

    /// <summary>
    /// Ask the OS for notification permission before scheduling reminders. We do
    /// *not* request exact-alarm capability: reminders are delivered as inexact
    /// allow-while-idle alarms (the plugin falls back to that automatically when the
    /// app can't schedule exact alarms), so the carer sees a single POST_NOTIFICATIONS
    /// prompt instead of a second detour to the system "Alarms &amp; reminders" screen.
    /// See AI/design-decisions.md -> "Reminders use inexact (allow-while-idle) alarms".
    /// </summary>
    public async Task<bool> RequestPermissionAsync()
    {
        var granted = await _notifications.RequestNotificationPermissionAsync(requestExactAlarm: false);

        // The answer just changed; don't let a stale cache suppress arming for the
        // next half minute.
        await _gate.WaitAsync();
        try { _deliveryEnabled = null; }
        finally { _gate.Release(); }

        return granted;
    }

    /// <summary>Whether the OS will actually deliver a reminder right now. A live check —
    /// the permission can be revoked in system settings at any time.</summary>
    public Task<bool> AreRemindersDeliverableAsync() => _notifications.AreNotificationsEnabledAsync();

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
            await SyncMedicationCoreAsync(medicationId, DateTime.Now);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SyncMedicationCoreAsync(int medicationId, DateTime now)
    {
        var medication = await _medicationService.GetMedicationByIdAsync(medicationId);
        if (medication == null || medication.IsArchived)
        {
            await CancelMedicationCoreAsync(medicationId);
            return;
        }

        // Paused on this device (AI/app-voice.md §15): stop arming reminders and clear
        // any already-armed ones. Nothing must fire for a paused pet — a reminder naming
        // a pet who died is the worst string this product can show. The record is
        // untouched; resuming re-arms from the saved schedule rules.
        if (_pause.IsPaused(medication.PetId))
        {
            await CancelMedicationCoreAsync(medicationId);
            return;
        }

        var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(medicationId);
        if (schedules.Count == 0)
        {
            await ClearPendingAsync(medicationId, now);
            return;
        }

        // Stable slot index per distinct time, for message rotation.
        var distinctTimes = schedules.Select(s => s.Time).Distinct().OrderBy(t => t).ToList();

        // Anchor the horizon on a DAY boundary rather than "now + 14×24h". A moving
        // end means the desired set changes on every launch, which would defeat the
        // already-armed fast path below.
        var horizonEnd = now.Date.AddDays(HorizonDays);

        // Expand every (weekday + time) rule into concrete occurrences.
        var occurrences = new List<(DateTime When, int Slot)>();
        foreach (var schedule in schedules)
        {
            var slot = distinctTimes.IndexOf(schedule.Time);
            foreach (var when in MedicationScheduleExpander.Expand(schedule.Day, schedule.Time, now, horizonEnd))
                occurrences.Add((when, slot));
        }

        var all = await _instances.GetByMedicationAsync(medicationId);

        // Respect the OS alarm budget: this med may only take what's left after every
        // OTHER medication's pending occurrences. (Its own are about to be replaced.)
        var pendingOthers = (await _instances.GetAllAsync())
            .Count(i => i.Status == ReminderStatus.Pending && i.MedicationId != medicationId);
        var budget = Math.Max(0, GlobalPendingBudget - pendingOthers);

        var ordered = occurrences
            .GroupBy(o => o.When)              // dedupe identical day+time across rules
            .Select(g => g.First())
            .OrderBy(o => o.When)
            .Take(Math.Min(MaxInstancesPerMedication, budget))
            .ToList();

        // Fast path: if exactly the right future occurrences are already armed, do
        // nothing. This is the common case on every launch, and skipping it avoids
        // cancelling + re-arming hundreds of alarms (each of which rewrites the
        // plugin's whole pending list in SharedPreferences) for no change at all.
        var armed = all
            .Where(i => i.Status == ReminderStatus.Pending && i.ScheduledTime > now)
            .Select(i => i.ScheduledTime)
            .OrderBy(t => t)
            .ToList();

        if (armed.Count == ordered.Count && !armed.Where((t, idx) => t != ordered[idx].When).Any())
            return;

        // Clean slate for FUTURE occurrences only; past-due ones are left for the
        // catch-up to resolve (see ClearPendingAsync).
        await ClearPendingAsync(medicationId, now);

        // Nothing the OS would accept right now → don't record instances claiming to be
        // armed. The next launch re-materializes once notifications are back on.
        if (!await DeliveryEnabledAsync())
            return;

        var pet = await _petService.GetPetByIdAsync(medication.PetId);
        var petName = pet?.Name ?? string.Empty;

        // Persist the whole batch atomically, then arm the one-shots.
        var instances = ordered.Select(o => new ReminderInstance
        {
            MedicationId = medicationId,
            ScheduledTime = o.When,
            SlotIndex = o.Slot,
            Status = ReminderStatus.Pending
        }).ToList();
        await _instances.InsertAllAsync(instances, NotificationIds.ForInstance);

        var rejected = new List<ReminderInstance>();
        foreach (var instance in instances)
        {
            var accepted = await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = instance.NotificationId,
                Title = NotificationMessages.MedicationTitle(petName),
                Message = NotificationMessages.MedicationBody(petName, medication.Name, instance.SlotIndex),
                NotifyTime = instance.ScheduledTime,
                Recurrence = NotificationRecurrence.Once,
                Channel = NotificationChannelKind.Medication,
            });

            // The OS refused it — drop the row rather than let the catch-up later
            // report it as delivered.
            if (!accepted)
                rejected.Add(instance);
        }

        if (rejected.Count > 0)
            await _instances.DeleteAllAsync(rejected);
    }

    // ── Per-pet pause / resume (AI/app-voice.md §15) ─────────────────────────

    /// <summary>
    /// Cancel every armed reminder for one pet's medications, without deleting any
    /// data. Called when the pet is paused on this device. The paused flag itself is
    /// owned by <see cref="PetPauseService"/> — the caller sets it before calling here,
    /// so the launch/boot catch-up also skips this pet.
    /// </summary>
    public async Task CancelPetAsync(int petId)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var med in await _medicationService.GetMedicationsByPetIdAsync(petId))
                await SafeAsync(() => CancelMedicationCoreAsync(med.Id), $"cancel med {med.Id}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Re-arm every reminder for one pet's medications from their saved
    /// schedule rules. Called on resume; a no-op for archived meds and (defensively)
    /// still paused pets, since <see cref="SyncMedicationCoreAsync"/> self-guards both.</summary>
    public async Task SyncPetAsync(int petId)
    {
        await _gate.WaitAsync();
        try
        {
            var now = DateTime.Now;
            foreach (var med in await _medicationService.GetMedicationsByPetIdAsync(petId))
                await SafeAsync(() => SyncMedicationCoreAsync(med.Id, now), $"sync med {med.Id}");
        }
        finally
        {
            _gate.Release();
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
        await _instances.DeleteAllByMedicationAsync(medicationId);
    }

    /// <summary>
    /// Drop this medication's <b>future</b> pending occurrences and their OS alarms.
    ///
    /// Deliberately time-filtered. A past-due Pending row is evidence that a
    /// notification was armed and has since come due — the catch-up needs it to decide
    /// whether the dose was missed while the device was off. Deleting those here let
    /// any re-sync (a cloud pull, a medication edit) racing the launch catch-up erase
    /// the missed-dose evidence before it was read. Cancelling them also had a second
    /// cost: the plugin's cancel removes an ALREADY-POSTED notification from the tray,
    /// so a background sync could silently clear a reminder the carer hadn't acted on.
    /// </summary>
    private async Task ClearPendingAsync(int medicationId, DateTime now)
    {
        var pending = (await _instances.GetByMedicationAsync(medicationId))
            .Where(i => i.Status == ReminderStatus.Pending && i.ScheduledTime > now)
            .ToList();

        if (pending.Count == 0)
            return;

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

        // A boot triggers BOTH the boot receiver's catch-up (resendMissed: true) and —
        // because starting the process constructs the MAUI Application, which runs the
        // normal startup path — the launch catch-up (resendMissed: false). They
        // serialize on _gate, but nothing orders them: if the launch pass won, it
        // resolved every past-due occurrence to Fired and the boot pass then had
        // nothing left to re-send. The doses missed while the phone was off vanished.
        //
        // The receiver therefore records the intent durably the moment the broadcast
        // arrives, and whichever pass runs first honours it.
        var bootPending = Preferences.Default.Get(BootRecoveryPendingKey, false);
        var resend = resendMissed || bootPending;

        await PurgeLegacyNotificationIdsAsync();

        var all = await _instances.GetAllAsync();

        var pendingPast = all
            .Where(i => i.Status == ReminderStatus.Pending && i.ScheduledTime <= now)
            .OrderBy(i => i.ScheduledTime)
            .ToList();

        var missedToResend = new List<ReminderInstance>();
        var resolved = new List<ReminderInstance>();
        foreach (var inst in pendingPast)
        {
            // On boot: anything scheduled within the off window (after the app was
            // last confirmed alive) could not have fired — re-send it. Otherwise
            // assume the OS delivered it and just mark it handled.
            var missed = resend && inst.ScheduledTime > lastSeen;

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
            resolved.Add(inst);
        }

        // One transaction instead of one round trip per instance — this loop runs
        // inside the boot receiver's very limited time budget.
        await _instances.UpdateAllAsync(resolved);

        await ResendMissedAsync(missedToResend);

        // The re-send has happened (or there was nothing to send); the boot intent is
        // discharged. Cleared only on a pass that actually honoured it, so a crash
        // before this point leaves it for the next pass to pick up.
        if (resend && bootPending)
            Preferences.Default.Remove(BootRecoveryPendingKey);

        // Re-materialize + re-arm every medication; cancel archived ones.
        // (Core variants — the gate is already held.) One medication failing must not
        // leave every later medication un-armed.
        var meds = await _medicationService.GetAllMedicationsAsync();
        foreach (var med in meds)
        {
            if (med.IsArchived)
                await SafeAsync(() => CancelMedicationCoreAsync(med.Id), $"cancel med {med.Id}");
            else
                await SafeAsync(() => SyncMedicationCoreAsync(med.Id, now), $"sync med {med.Id}");
        }

        // Record durable "missed" adherence for past doses never logged.
        await SafeAsync(() => _doseReconciler.ReconcileMissedAsync(now), "reconcile");

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

    private async Task ResendMissedAsync(List<ReminderInstance> missed)
    {
        if (missed.Count == 0)
            return;

        // One clear catch-up per medication, rather than a flood of stale alerts.
        foreach (var group in missed.GroupBy(m => m.MedicationId))
        {
            var med = await _medicationService.GetMedicationByIdAsync(group.Key);
            if (med == null || med.IsArchived || _pause.IsPaused(med.PetId))
                continue;

            var pet = await _petService.GetPetByIdAsync(med.PetId);
            var petName = pet?.Name ?? string.Empty;

            await _notifications.ScheduleNotification(new NotificationContent
            {
                Id = NotificationIds.MissedDose(med.Id),
                Title = NotificationMessages.MedicationMissedTitle(petName),
                Message = NotificationMessages.MedicationMissedBody(petName, med.Name, group.Count()),
                // Read the clock HERE, not once at the top of the catch-up. The plugin
                // drops any notify time older than ~1 minute, and everything between
                // the start of the pass and this point (resolving a long backlog on a
                // cold boot) is exactly what pushes it past that — silently losing the
                // missed-dose alert on the slow boots it exists for.
                NotifyTime = DateTime.Now.AddSeconds(2),
                Recurrence = NotificationRecurrence.Once,
                Channel = NotificationChannelKind.Medication,
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

    /// <summary>
    /// One-time sweep of the ID range used by the pre-instance daily-repeat scheme.
    /// Cancelling those ids on every medication cancel, forever, cost two
    /// SharedPreferences rewrites each for notifications that can no longer exist on
    /// any install that has run this once.
    /// </summary>
    private async Task PurgeLegacyNotificationIdsAsync()
    {
        if (Preferences.Default.Get(LegacyIdsPurgedKey, false))
            return;

        try
        {
            var meds = await _medicationService.GetAllMedicationsAsync();
            foreach (var med in meds)
                await _notifications.CancelNotifications(NotificationIds.AllMedicationReminders(med.Id));

            Preferences.Default.Set(LegacyIdsPurgedKey, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reminders] legacy id purge failed: {ex.Message}");
        }
    }

    private async Task<bool> DeliveryEnabledAsync()
    {
        // Cached for the length of one pass: a catch-up asks once per medication and
        // the answer cannot meaningfully change mid-pass.
        if (_deliveryEnabled is bool cached && DateTime.Now - _deliveryEnabledAt < TimeSpan.FromSeconds(30))
            return cached;

        var enabled = await _notifications.AreNotificationsEnabledAsync();
        _deliveryEnabled = enabled;
        _deliveryEnabledAt = DateTime.Now;
        return enabled;
    }

    private static async Task SafeAsync(Func<Task> work, string what)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reminders] {what} failed: {ex.Message}");
        }
    }

    private static DateTime GetLastSeen(DateTime fallback)
    {
        var ticks = Preferences.Default.Get(LastSeenKey, 0L);
        return ticks == 0 ? fallback : new DateTime(ticks, DateTimeKind.Local);
    }

    private static void SetLastSeen(DateTime when)
        => Preferences.Default.Set(LastSeenKey, when.Ticks);

    /// <summary>
    /// Record that the app was alive just now. Called when the app goes to the
    /// background, which is the last moment we can be sure of it.
    ///
    /// Without this the marker only moved during a catch-up, i.e. at cold start — so a
    /// carer who used the app all week and then rebooted got a "device was off for six
    /// days" window, and every unlogged dose in it was re-sent as missed.
    ///
    /// Deliberately NOT called on resume: a cold start can raise resume before the
    /// launch catch-up reads the marker, which would collapse a genuine device-off
    /// window to zero and swallow real missed doses.
    /// </summary>
    public static void MarkSeen() => SetLastSeen(DateTime.Now);

    /// <summary>
    /// Record that a device boot needs a missed-dose re-send, before any async work.
    /// Read (and cleared) by the next catch-up to run, whichever entry point starts it.
    /// </summary>
    public static void MarkBootRecoveryPending()
        => Preferences.Default.Set(BootRecoveryPendingKey, true);

    /// <summary>Forget the persisted markers. Called by the full data reset so a fresh
    /// start can't inherit the old install's catch-up window.</summary>
    public static void ClearPersistedState()
    {
        Preferences.Default.Remove(LastSeenKey);
        Preferences.Default.Remove(BootRecoveryPendingKey);
    }
}
