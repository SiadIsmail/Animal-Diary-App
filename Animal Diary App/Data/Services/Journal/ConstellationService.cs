namespace Animal_Diary_App.Data.Services.Journal;

using System.Globalization;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Helpers;

// ─────────────────────────────────────────────────────────────────────────────
//  Everything one pet has had written down over a stretch of time, flattened into
//  one time-ordered list of stars.
//
//  This is the Journal's day gather (JournalLogViewModel.GatherTimelineAsync) taken
//  wide instead of deep: same stores, same "one list, sorted purely by time" rule,
//  a range instead of a day. It is a separate service rather than another method on
//  that ViewModel because the two answer different questions and would otherwise
//  share nothing but their table names, and because a range read has to be issued
//  as ONE round of parallel queries or a year of history becomes ten sequential
//  scans (AI/coding-standards.md).
//
//  What it deliberately does NOT do: sort by anything but time, group, bucket by
//  day, count, total, or rank. Those are all shapes that would let the sky imply
//  something about the readings, and the sky says only that they happened.
// ─────────────────────────────────────────────────────────────────────────────

public class ConstellationService
{
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly WaterEntryService _water;
    private readonly SeizureEntryService _seizures;
    private readonly CustomTrackerService _custom;
    private readonly MedicationService _medications;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly DisplayUnitService _displayUnits;

    public ConstellationService(
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        WaterEntryService water,
        SeizureEntryService seizures,
        CustomTrackerService custom,
        MedicationService medications,
        MedicationDoseLogService doseLogs,
        DisplayUnitService displayUnits)
    {
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _water = water;
        _seizures = seizures;
        _custom = custom;
        _medications = medications;
        _doseLogs = doseLogs;
        _displayUnits = displayUnits;
    }

    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Everything recorded for <paramref name="petId"/> between two dates
    /// (inclusive, date-only), in ascending time order.</summary>
    public async Task<List<CelestialEvent>> GetRangeAsync(int petId, DateTime from, DateTime to)
    {
        var events = new List<CelestialEvent>();
        if (petId <= 0)
            return events;

        from = from.Date;
        to = to.Date;

        // One range scan at a time. Issuing them together read as parallelism but was
        // not: sqlite-net's async API queues every call to the thread pool and then
        // serializes them on the one shared connection, so ten concurrent range scans
        // meant ten pooled threads and nine of them blocked on a lock. The scans ran in
        // sequence regardless. Each is covered by a composite (PetId, Date) index: if
        // this page ever needs fewer round trips, the answer is a wider query.
        var entries = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);
        var glucoseEntries = await _glucose.GetForRangeAsync(petId, from, to);
        var appetiteEntries = await _appetite.GetForRangeAsync(petId, from, to);
        var appetiteAmounts = await _appetite.GetAmountsForRangeAsync(petId, from, to);
        var waterAmounts = await _water.GetAmountsForRangeAsync(petId, from, to);
        var waterLevels = await _water.GetLevelsForRangeAsync(petId, from, to);
        var seizureEntries = await _seizures.GetForRangeAsync(petId, from, to);
        // One query for every owner-defined tracker there is, grouped in memory,
        // the same thing that makes "as many as you like" affordable in the Journal.
        var customEntries = await _custom.GetForRangeAsync(petId, from, to);
        var customDefs = await _custom.GetAllForPetAsync(petId);
        var meds = await _medications.GetMedicationsByPetIdAsync(petId);

        // The units the owner's own entries resolved to. Read once for the whole range:
        // this gather feeds BOTH the sky's tapped-entry detail and the free plain export,
        // and neither may show one weigh-in in one unit and the next in another.
        var weightUnit = await _displayUnits.ResolveAsync(petId, UnitFamily.Weight);
        var glucoseUnit = await _displayUnits.ResolveAsync(petId, UnitFamily.Glucose);
        var durationUnit = await _displayUnits.ResolveAsync(petId, UnitFamily.Duration);
        var volumeUnit = await _displayUnits.ResolveAsync(petId, UnitFamily.Volume);
        var foodUnit = await _displayUnits.ResolveAsync(petId, UnitFamily.FoodMass);

        // ── Mood + weight: two independent readings sharing the day's PetEntry row,
        //    each with its own recorded time. A legacy row without one sits at the
        //    start of its day rather than being dropped: it still happened.
        foreach (var entry in entries)
        {
            if (entry.MoodLevel > 0)
            {
                var mood = (MoodLevel)entry.MoodLevel;
                events.Add(new CelestialEvent(
                    At(entry.Date, entry.MoodTimeTicks),
                    CelestialCategory.Mood,
                    Loc.GetString("Journal_MoodTitle"),
                    mood.GetDisplayName()));
            }

            if (entry.Weight > 0)
            {
                events.Add(new CelestialEvent(
                    At(entry.Date, entry.WeightTimeTicks),
                    CelestialCategory.Weight,
                    Loc.GetString("Journal_WeighIn"),
                    UnitText.WithUnit(entry.Weight, weightUnit)));
            }
        }

        foreach (var g in glucoseEntries)
        {
            events.Add(new CelestialEvent(
                At(g.Date, g.Time),
                CelestialCategory.Glucose,
                Loc.GetString("Journal_GlucoseCheck"),
                Loc.Format("Journal_GlucoseTimeline", UnitText.WithUnit(g.Value, glucoseUnit))));
        }

        // Appetite and water each arrive in two shapes: a measured amount and a
        // relative word. They stay ONE symbol here and are never merged into one
        // value: the two kinds sit side by side in the detail line exactly as they
        // sit in separate graphs in the vet report (AI/design-decisions.md).
        foreach (var a in appetiteEntries)
        {
            var word = ((AppetiteLevel)a.Level).GetDisplayName().ToLowerInvariant();
            events.Add(new CelestialEvent(
                At(a.Date, a.Time),
                CelestialCategory.Appetite,
                Loc.GetString("Journal_Appetite"),
                WithFood(Loc.Format("Journal_AteWord", word), a.Food)));
        }

        foreach (var a in appetiteAmounts)
        {
            events.Add(new CelestialEvent(
                At(a.Date, a.Time),
                CelestialCategory.Appetite,
                Loc.GetString("Journal_Appetite"),
                WithFood(UnitText.WithUnit(a.Grams, foodUnit), a.Food)));
        }

        foreach (var w in waterAmounts)
        {
            events.Add(new CelestialEvent(
                At(w.Date, w.Time),
                CelestialCategory.Water,
                Loc.GetString("Journal_Water"),
                UnitText.WithUnit(w.AmountMl, volumeUnit)));
        }

        foreach (var w in waterLevels)
        {
            var word = ((WaterLevel)w.Level).GetDisplayName().ToLowerInvariant();
            events.Add(new CelestialEvent(
                At(w.Date, w.Time),
                CelestialCategory.Water,
                Loc.GetString("Journal_Water"),
                Loc.Format("Journal_DrankWord", word)));
        }

        foreach (var s in seizureEntries)
        {
            events.Add(new CelestialEvent(
                At(s.Date, s.Time),
                CelestialCategory.Seizure,
                Loc.GetString("Journal_Seizure"),
                SeizureDetail(s, durationUnit)));
        }

        // Owner-defined trackers, including RETIRED ones: an entry outlives the
        // retirement of the tracker that collected it, and reading only the live list
        // would blank out April the moment someone tidied their care plan.
        var defs = customDefs.ToDictionary(d => d.Id);
        foreach (var c in customEntries)
        {
            var def = defs.GetValueOrDefault(c.CustomTrackerId);
            events.Add(new CelestialEvent(
                At(c.Date, c.Time),
                CelestialCategory.Custom,
                def?.Name ?? string.Empty,
                CustomDetail(c, def)));
        }

        events.AddRange(await GatherDosesAsync(meds, from, to));

        // The one ordering: everything, purely by time. Same rule as the Journal's
        // day timeline: there are no per-kind lanes here and there must never be.
        events.Sort((a, b) => a.When.CompareTo(b.When));
        return events;
    }

    /// <summary>
    /// The doses the OWNER recorded (taken or skipped) placed at the moment they
    /// tapped, falling back to the scheduled time (the same rule the Journal timeline
    /// uses for a dose card).
    ///
    /// <para><b>A <c>Missed</c> row is deliberately not a star.</b> Nobody wrote it
    /// down: <c>MedicationDoseReconciler</c> stamps it automatically for any scheduled
    /// dose with no log, so it is the app's inference, not the owner's record. Density
    /// here means "how much was recorded", and letting an unopened app fill the sky
    /// would break that: as well as scattering absences across a surface someone
    /// opens to look at their animal's life. The vet report is where a missed dose is
    /// counted, and it says so in words (AI/known-constraints.md).</para>
    /// </summary>
    private async Task<List<CelestialEvent>> GatherDosesAsync(
        IReadOnlyList<Medication> medications, DateTime from, DateTime to)
    {
        var stars = new List<CelestialEvent>();
        if (medications.Count == 0)
            return stars;

        var byId = medications.ToDictionary(m => m.Id);
        var logs = await _doseLogs.GetByMedicationsAndRangeAsync(byId.Keys.ToList(), from, to);

        foreach (var log in logs)
        {
            if (log.Status is not (DoseStatus.Taken or DoseStatus.Skipped))
                continue;

            var name = byId.GetValueOrDefault(log.MedicationId)?.Name ?? string.Empty;
            stars.Add(new CelestialEvent(
                log.ResolvedAt ?? log.ScheduledDate.Date + log.ScheduledTime,
                CelestialCategory.Medication,
                name,
                Loc.GetString(log.Status == DoseStatus.Taken ? "Sky_DoseGiven" : "Sky_DoseSkipped")));
        }

        return stars;
    }

    // ── Detail lines (read out on tap only; never drawn into the sky) ─────────────

    private static string SeizureDetail(SeizureEntry entry, UnitDef durationUnit)
    {
        var parts = new List<string>(3);

        var type = entry.Type.GetDisplayName();
        if (!string.IsNullOrEmpty(type))
            parts.Add(type);

        if (entry.DurationSeconds is int seconds)
            parts.Add(Loc.Format(
                "Journal_SeizureDuration", UnitText.WithUnit(seconds, durationUnit)));

        if (!string.IsNullOrWhiteSpace(entry.Note))
            parts.Add(entry.Note);

        return string.Join(" · ", parts);
    }

    private static string CustomDetail(CustomEntry entry, CustomTracker? def)
    {
        var parts = new List<string>(2);

        if (entry.Amount is decimal amount)
        {
            var value = amount.ToString("0.#", CultureInfo.CurrentCulture);
            // The unit the ENTRY was written in (see CustomEntry.UnitFor): this gather
            // also feeds the free plain export, which is the one document that promises
            // to be exactly what the owner wrote down.
            var unit = entry.UnitFor(def);
            parts.Add(unit.Length == 0 ? value : $"{value} {unit}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Note))
            parts.Add(entry.Note);

        return string.Join(" · ", parts);
    }

    private static string WithFood(string line, string? food) =>
        string.IsNullOrWhiteSpace(food) ? line : $"{line} · {food}";

    // ── Time helpers ─────────────────────────────────────────────────────────────

    private static DateTime At(DateTime date, TimeSpan time) => date.Date + time;

    /// <summary>A legacy mood/weight row saved before per-entry times existed has no
    /// time; it sits at the start of its own day rather than being invented a
    /// plausible one. The app does not make up a moment it was never told.</summary>
    private static DateTime At(DateTime date, long? ticks) =>
        ticks.HasValue ? date.Date + TimeSpan.FromTicks(ticks.Value) : date.Date;
}
