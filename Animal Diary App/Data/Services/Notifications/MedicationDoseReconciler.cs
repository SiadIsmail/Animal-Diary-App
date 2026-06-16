namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

/// <summary>
/// Writes durable <see cref="DoseStatus.Missed"/> records for scheduled doses
/// that came and went without being logged Taken or Skipped. Runs on app launch
/// and device boot (via <see cref="MedicationReminderScheduler.CatchUpAndRefreshAsync"/>).
///
/// The sweep is idempotent: it only writes a Missed row where no log exists, so
/// re-running it never duplicates or overwrites a user's Taken/Skipped record.
/// It is bounded two ways so it can never fabricate history:
///   • a <see cref="BackfillDays"/> look-back window, and
///   • each medication's <see cref="Medication.CreatedAt"/> (never before the
///     medication existed).
/// </summary>
public class MedicationDoseReconciler
{
    // How far back a sweep will look for un-logged past doses.
    private const int BackfillDays = 14;

    // A dose isn't "missed" until it's overdue by more than this — mirrors the
    // reminder grace window so a just-due dose isn't prematurely marked.
    private static readonly TimeSpan Grace = TimeSpan.FromHours(2);

    private readonly MedicationService _medicationService;
    private readonly MedicationDoseLogService _doseLogService;

    public MedicationDoseReconciler(MedicationService medicationService, MedicationDoseLogService doseLogService)
    {
        _medicationService = medicationService;
        _doseLogService = doseLogService;
    }

    public async Task ReconcileMissedAsync(DateTime now)
    {
        var cutoff = now - Grace;                  // only doses on/before this are eligible
        var windowStart = now.AddDays(-BackfillDays);

        var meds = (await _medicationService.GetAllMedicationsAsync())
            .Where(m => !m.IsArchived)
            .ToList();

        foreach (var med in meds)
        {
            // Never look earlier than the medication's own creation.
            var medStart = med.CreatedAt > windowStart ? med.CreatedAt : windowStart;
            if (medStart >= cutoff)
                continue;

            var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(med.Id);
            if (schedules.Count == 0)
                continue;

            // Existing logs in the window — any (date, time) here is already resolved.
            var existing = await _doseLogService.GetByMedicationAndRangeAsync(med.Id, medStart.Date, cutoff.Date);
            var resolved = new HashSet<(DateTime Date, TimeSpan Time)>(
                existing.Select(l => (l.ScheduledDate.Date, l.ScheduledTime)));

            foreach (var schedule in schedules)
            {
                foreach (var occurrence in MedicationScheduleExpander.Expand(schedule.Day, schedule.Time, medStart, cutoff))
                {
                    var key = (occurrence.Date, occurrence.TimeOfDay);
                    if (!resolved.Add(key))
                        continue;   // already logged, or already marked in this pass

                    await _doseLogService.SetStatusAsync(med.Id, med.PetId, occurrence.Date, occurrence.TimeOfDay, DoseStatus.Missed);
                }
            }
        }
    }
}
