namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// Gathers a live snapshot for a pet + day and runs it through the pure
/// <see cref="PendingEngine"/>. This is the async half; all the medical judgement
/// stays in the engine so it can be unit-tested. Keeping this separate from the
/// CalendarViewModel is deliberate — the pending list is new functionality, so it
/// lives in its own service.
/// </summary>
public class PendingItemsService
{
    private readonly CarePlanService _carePlan;
    private readonly MedicationService _medications;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;

    public PendingItemsService(
        CarePlanService carePlan,
        MedicationService medications,
        MedicationDoseLogService doseLogs,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite)
    {
        _carePlan = carePlan;
        _medications = medications;
        _doseLogs = doseLogs;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
    }

    /// <summary>What's still to do for this pet, today.</summary>
    public async Task<IReadOnlyList<PendingItem>> GetAsync(Pet pet, DateTime date)
    {
        var day = date.Date;
        var plan = await _carePlan.GetPlanAsync(pet);

        var doses = await GatherDosesAsync(pet.Id, day);
        var entries = await GatherEntryDatesAsync(pet.Id, day);

        return PendingEngine.Compute(plan, doses, entries, day);
    }

    // Today's scheduled doses, each flagged with whether it has been acted on. A
    // dose is "given" once it has any dose-log row (Taken / Skipped / Missed) — a
    // deliberately-skipped dose shouldn't keep nagging. Insulin is not special.
    private async Task<IReadOnlyList<ScheduledDose>> GatherDosesAsync(int petId, DateTime day)
    {
        var meds = (await _medications.GetMedicationsByPetIdAsync(petId))
            .Where(m => !m.IsArchived)
            .ToList();

        if (meds.Count == 0)
            return System.Array.Empty<ScheduledDose>();

        var schedulesByMed = (await _medications.GetSchedulesForMedicationsAsync(meds.Select(m => m.Id).ToList()))
            .ToLookup(s => s.MedicationId);
        var logs = await _doseLogs.GetByPetAndDateAsync(petId, day);

        var result = new List<ScheduledDose>();
        foreach (var med in meds)
        {
            var times = schedulesByMed[med.Id]
                .Where(s => s.Day == day.DayOfWeek)
                .Select(s => s.Time)
                .Distinct();

            foreach (var time in times)
            {
                var given = logs.Any(l => l.MedicationId == med.Id && l.ScheduledTime == time);
                result.Add(new ScheduledDose(med.Id, petId, med.Name, time, given));
            }
        }

        return result;
    }

    // Each tracker's recent entry dates (rolling 7 days, enough for the weekly
    // window). Mood + weight live on PetEntry; glucose + appetite in their own
    // tables. Trackers without a store here simply contribute no dates.
    private async Task<IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>>> GatherEntryDatesAsync(int petId, DateTime day)
    {
        var from = day.AddDays(-6);

        var petEntries = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, day);
        var glucose = await _glucose.GetForRangeAsync(petId, from, day);
        var appetite = await _appetite.GetForRangeAsync(petId, from, day);

        return new Dictionary<TrackerId, IReadOnlyList<DateTime>>
        {
            [TrackerId.Mood] = petEntries.Where(e => e.MoodLevel > 0).Select(e => e.Date.Date).ToList(),
            [TrackerId.Weight] = petEntries.Where(e => e.Weight > 0).Select(e => e.Date.Date).ToList(),
            [TrackerId.Glucose] = glucose.Select(g => g.Date.Date).ToList(),
            [TrackerId.Appetite] = appetite.Select(a => a.Date.Date).ToList(),
        };
    }
}
