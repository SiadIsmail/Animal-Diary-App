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
    private readonly DayDoseService _dayDoses;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;

    public PendingItemsService(
        CarePlanService carePlan,
        DayDoseService dayDoses,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite)
    {
        _carePlan = carePlan;
        _dayDoses = dayDoses;
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
    // deliberately-skipped dose shouldn't keep nagging. Insulin is not special. The
    // meds → schedules → logs join is shared with the Journal timeline + Calendar
    // via DayDoseService; here we project it to the engine's flat snapshot.
    private async Task<IReadOnlyList<ScheduledDose>> GatherDosesAsync(int petId, DateTime day)
    {
        var doses = await _dayDoses.GetForDayAsync(petId, day);
        return doses
            .Select(d => new ScheduledDose(d.Medication.Id, petId, d.Medication.Name, d.ScheduledTime, d.Given))
            .ToList();
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
