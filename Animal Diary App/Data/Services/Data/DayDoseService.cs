namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;

/// <summary>One dose scheduled for a pet on a given day, joined with its recorded
/// outcome (<see cref="Log"/> is null when the dose hasn't been acted on). The single
/// shape behind "what doses does this pet have today, and what happened to each?".</summary>
public readonly record struct DayDose(Medication Medication, TimeSpan ScheduledTime, MedicationDoseLog? Log)
{
    /// <summary>True once the dose has any outcome recorded (Taken / Skipped / Missed).</summary>
    public bool Given => Log != null;
}

/// <summary>
/// The single place that expands a pet's medication schedules for ONE day and joins
/// them with that day's dose logs. Every caller that needs the day's doses — the
/// Journal timeline, the Calendar dose checklist and the pending engine — projects
/// this into its own shape, so the meds → schedules → logs join lives here once
/// instead of being copied into each. (Whole-WEEK expansion for the month dots is a
/// different computation and stays in the CalendarViewModel.)
/// </summary>
public class DayDoseService
{
    private readonly MedicationService _medications;
    private readonly MedicationDoseLogService _doseLogs;

    public DayDoseService(MedicationService medications, MedicationDoseLogService doseLogs)
    {
        _medications = medications;
        _doseLogs = doseLogs;
    }

    /// <summary>Every dose scheduled for the pet on <paramref name="date"/> (each
    /// non-archived medication's schedule rules for that weekday), joined with the
    /// day's dose logs. Unordered — callers sort as they need.</summary>
    public async Task<List<DayDose>> GetForDayAsync(int petId, DateTime date)
    {
        var result = new List<DayDose>();
        if (petId == 0)
            return result;

        var meds = (await _medications.GetMedicationsByPetIdAsync(petId))
            .Where(m => !m.IsArchived)
            .ToList();
        if (meds.Count == 0)
            return result;

        var day = date.Date;
        var weekday = day.DayOfWeek;
        var schedulesByMed = (await _medications.GetSchedulesForMedicationsAsync(meds.Select(m => m.Id).ToList()))
            .ToLookup(s => s.MedicationId);
        var logs = await _doseLogs.GetByPetAndDateAsync(petId, day);

        foreach (var med in meds)
        {
            var times = schedulesByMed[med.Id].Where(s => s.Day == weekday).Select(s => s.Time).Distinct();
            foreach (var time in times)
            {
                var log = logs.FirstOrDefault(l => l.MedicationId == med.Id && l.ScheduledTime == time);
                result.Add(new DayDose(med, time, log));
            }
        }

        return result;
    }
}
