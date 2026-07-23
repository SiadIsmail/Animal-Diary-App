namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// Gathers a live snapshot for a pet + day and runs it through the pure
/// <see cref="PendingEngine"/>. This is the async half; all the medical judgement
/// stays in the engine so it can be unit-tested. Keeping this separate from the
/// CalendarViewModel is deliberate — the pending list is new functionality, so it
/// lives in its own service.
/// </summary>
/// <summary>Everything the Today page needs about the day's care in one snapshot:
/// the pending list and the ring's progress counts (see
/// <see cref="PendingItemsService.GetTodayCareAsync"/>).</summary>
public sealed record TodayCare(IReadOnlyList<PendingItem> Pending, DayProgress Progress);

public class PendingItemsService
{
    private readonly CarePlanService _carePlan;
    private readonly DayDoseService _dayDoses;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly WaterEntryService _water;

    public PendingItemsService(
        CarePlanService carePlan,
        DayDoseService dayDoses,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        WaterEntryService water)
    {
        _carePlan = carePlan;
        _dayDoses = dayDoses;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _water = water;
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

    /// <summary>
    /// The Today page's snapshot: the pending list (same rules as the Journal's
    /// chips) plus the care ring's done/total counts, computed from ONE gather so
    /// the two can never disagree. Water is a full tracker like the rest now — its
    /// next-up card routes to the Journal's water sheet, so it counts toward the
    /// ring exactly like glucose or appetite.
    /// </summary>
    public async Task<TodayCare> GetTodayCareAsync(Pet pet, DateTime date)
    {
        var day = date.Date;
        var plan = await _carePlan.GetPlanAsync(pet);

        var doses = await GatherDosesAsync(pet.Id, day);
        var entries = await GatherEntryDatesAsync(pet.Id, day);

        return new TodayCare(
            PendingEngine.Compute(plan, doses, entries, day),
            PendingEngine.ComputeProgress(plan, doses, entries, day));
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
    // window). Mood + weight live on PetEntry; glucose, appetite + water in their
    // own tables. Trackers without a store here simply contribute no dates.
    private async Task<IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>>> GatherEntryDatesAsync(int petId, DateTime day)
    {
        var from = day.AddDays(-6);

        var petEntries = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, day);
        var glucose = await _glucose.GetForRangeAsync(petId, from, day);
        var appetite = await _appetite.GetForRangeAsync(petId, from, day);
        var appetiteAmounts = await _appetite.GetAmountsForRangeAsync(petId, from, day);
        var waterAmounts = await _water.GetAmountsForRangeAsync(petId, from, day);
        var waterLevels = await _water.GetLevelsForRangeAsync(petId, from, day);

        return new Dictionary<TrackerId, IReadOnlyList<DateTime>>
        {
            [TrackerId.Mood] = petEntries.Where(e => e.MoodLevel > 0).Select(e => e.Date.Date).ToList(),
            [TrackerId.Weight] = petEntries.Where(e => e.Weight > 0).Select(e => e.Date.Date).ToList(),
            [TrackerId.Glucose] = glucose.Select(g => g.Date.Date).ToList(),
            // A day counts as fed if EITHER a qualitative reading or a measured amount
            // was logged — the union of both appetite stores' dates.
            [TrackerId.Appetite] = appetite.Select(a => a.Date.Date)
                .Concat(appetiteAmounts.Select(a => a.Date.Date)).ToList(),
            // A day counts as "watered" if EITHER an exact ml reading or a relative
            // reading was logged — the union of both stores' dates.
            [TrackerId.Water] = waterAmounts.Select(w => w.Date.Date)
                .Concat(waterLevels.Select(w => w.Date.Date)).ToList(),
        };
    }
}
