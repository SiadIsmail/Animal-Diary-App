namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
//  What the owner wrote down about one record, over one stretch of time.
//
//  A READ MODEL, not a screen. It introduces no table, stores nothing, and reads
//  the entry stores that already own each record — the same posture as
//  ConstellationService and TodayCardService.
//
//  Being one service consumed by three surfaces (Today's card sheet, the
//  Constellation legend, the appointment summary) is what makes "computed
//  identically for every kind" true by construction. A second copy of this
//  arithmetic anywhere is how a statement that only appears for seizures, or only
//  when the numbers are interesting, gets in — and that is exactly the line this
//  feature is not allowed to cross. The doctrine itself lives on RecordFacts.
//
//  What it deliberately does NOT compute: an average, a total across unlike
//  readings, a trend, a delta, a direction, a ranking, or a "peak window".
// ─────────────────────────────────────────────────────────────────────────────

public class RecordFactsService
{
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly WaterEntryService _water;
    private readonly SeizureEntryService _seizures;
    private readonly CustomTrackerService _custom;
    private readonly MedicationService _medications;
    private readonly MedicationDoseLogService _doseLogs;

    public RecordFactsService(
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        WaterEntryService water,
        SeizureEntryService seizures,
        CustomTrackerService custom,
        MedicationService medications,
        MedicationDoseLogService doseLogs)
    {
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _water = water;
        _seizures = seizures;
        _custom = custom;
        _medications = medications;
        _doseLogs = doseLogs;
    }

    /// <summary>
    /// The facts for one record over <paramref name="from"/>..<paramref name="to"/>
    /// (inclusive, date-only).
    ///
    /// <para>Never throws: a record whose store cannot be read yields the same
    /// zero-count snapshot as one that is genuinely empty, because the surfaces that
    /// show this have nothing useful to say about the difference.</para>
    /// </summary>
    public async Task<RecordFacts> GetAsync(Pet? pet, TodayCardKey kind, DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;

        if (pet is null || pet.Id == 0)
            return RecordFacts.Empty(kind, from, to);

        try
        {
            if (kind.IsCustom)
                return await CustomAsync(pet.Id, kind, from, to);

            return kind.BuiltIn switch
            {
                TodayCardId.Weight => await WeightAsync(pet.Id, kind, from, to),
                TodayCardId.Mood => await MoodAsync(pet.Id, kind, from, to),
                TodayCardId.Glucose => await GlucoseAsync(pet.Id, kind, from, to),
                TodayCardId.Appetite => await AppetiteAsync(pet.Id, kind, from, to),
                TodayCardId.Water => await WaterAsync(pet.Id, kind, from, to),
                TodayCardId.Seizure => await SeizureAsync(pet.Id, kind, from, to),
                TodayCardId.Medication => await MedicationAsync(pet.Id, kind, from, to),
                _ => RecordFacts.Empty(kind, from, to),
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecordFacts] {kind} failed: {ex.Message}");
            return RecordFacts.Empty(kind, from, to);
        }
    }

    // ── One-per-day stores: mood and weight share the day's PetEntry row ──────
    //
    // Both count DAYS RECORDED, because that is what the store holds — one row per
    // pet per day, re-logging replaces it. A legacy row with no recorded time sits at
    // the start of its day, exactly as the Constellation places it: it still happened.

    private async Task<RecordFacts> WeightAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);
        return RecordFactsBuilder.Build(kind, from, to,
            NoMoments,
            rows.Where(e => e.Weight > 0)
                .Select(e => new RecordMoment(At(e.Date, e.WeightTimeTicks), e.Weight)));
    }

    private async Task<RecordFacts> MoodAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);

        // No value: a mood is a word (see MoodLevel). Passing the stored 1–5 here would
        // hand "Lowest 1 · Highest 4" to a surface that must never turn an observation
        // into a number (AI/design-decisions.md, "Communication layer").
        return RecordFactsBuilder.Build(kind, from, to,
            NoMoments,
            rows.Where(e => e.MoodLevel > 0)
                .Select(e => new RecordMoment(At(e.Date, e.MoodTimeTicks), null)));
    }

    // ── Event stores ─────────────────────────────────────────────────────────

    private async Task<RecordFacts> GlucoseAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _glucose.GetForRangeAsync(petId, from, to);
        return RecordFactsBuilder.Build(kind, from, to,
            rows.Select(g => new RecordMoment(g.Date.Date + g.Time, g.Value)),
            NoMoments);
    }

    private async Task<RecordFacts> SeizureAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _seizures.GetForRangeAsync(petId, from, to);

        // Duration is deliberately NOT the value. It is an attribute of the occurrence,
        // not the reading, and "Lowest 1 · Highest 6" minutes reads as a severity scale
        // the app has no business implying.
        return RecordFactsBuilder.Build(kind, from, to,
            rows.Select(s => new RecordMoment(s.Date.Date + s.Time, null)),
            NoMoments);
    }

    // ── The two-store records ────────────────────────────────────────────────
    //
    // Water and appetite each have a MEASURED store (additive events — mL, grams) and
    // an OBSERVED store (one relative word per day). The two are never merged into a
    // value or a chart, and an observation is never converted to a number: only the
    // measured side carries a Value here, so Lowest/Highest can only ever be mL or
    // grams. What IS combined is the COUNT — and only because a count is a fact about
    // the diary ("you wrote something down about water 40 times"), not about the
    // animal, and is reached the same way for every kind.

    private async Task<RecordFacts> AppetiteAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var amounts = await _appetite.GetAmountsForRangeAsync(petId, from, to);
        var levels = await _appetite.GetForRangeAsync(petId, from, to);

        return RecordFactsBuilder.Build(kind, from, to,
            amounts.Select(a => new RecordMoment(a.Date.Date + a.Time, a.Grams)),
            levels.Select(l => new RecordMoment(l.Date.Date + l.Time, null)));
    }

    private async Task<RecordFacts> WaterAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var amounts = await _water.GetAmountsForRangeAsync(petId, from, to);
        var levels = await _water.GetLevelsForRangeAsync(petId, from, to);

        return RecordFactsBuilder.Build(kind, from, to,
            amounts.Select(a => new RecordMoment(a.Date.Date + a.Time, a.AmountMl)),
            levels.Select(l => new RecordMoment(l.Date.Date + l.Time, null)));
    }

    // ── Owner-defined trackers ───────────────────────────────────────────────

    private async Task<RecordFacts> CustomAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = (await _custom.GetForRangeAsync(petId, from, to))
            .Where(e => e.CustomTrackerId == kind.CustomId);

        // An Amount tracker's number is in the OWNER's unit, so lowest and highest are
        // theirs to read; a Tick tracker has no number at all and Amount is null, which
        // the builder simply leaves out. Nothing has to know which shape it is.
        return RecordFactsBuilder.Build(kind, from, to,
            rows.Select(e => new RecordMoment(e.Date.Date + e.Time, e.Amount)),
            NoMoments);
    }

    // ── Doses ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pet's doses over the range: given, skipped, not recorded.
    ///
    /// <para>The dose slots are the UNION of (a) the current schedule rules walked over
    /// the period and (b) every dose log in it — the same rule the vet report follows,
    /// and for the same reason: schedule rows describe only the CURRENT rules, so
    /// counting from them alone lets an edit silently rewrite history, while each log
    /// row proves a dose was scheduled then. Bounded below by each medication's creation
    /// date and above by today, so a past day can never show phantom doses and a future
    /// one is not yet "not recorded".</para>
    /// </summary>
    private async Task<RecordFacts> MedicationAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        // Archived medications are included when they were still dosed in the period —
        // a range of history needs the whole picture, exactly as the report does.
        var meds = await _medications.GetMedicationsByPetIdAsync(petId);
        var medIds = meds.Select(m => m.Id).ToList();
        var schedules = (await _medications.GetSchedulesForMedicationsAsync(medIds))
            .ToLookup(s => s.MedicationId);
        var logs = (await _doseLogs.GetByMedicationsAndRangeAsync(medIds, from, to))
            .ToLookup(l => l.MedicationId);

        var moments = new List<RecordMoment>();
        int given = 0, skipped = 0, notRecorded = 0;

        foreach (var med in meds)
        {
            var start = med.CreatedAt.Date > from ? med.CreatedAt.Date : from;
            var end = to < DateTime.Today ? to : DateTime.Today;
            if (end < start)
                continue;

            var medLogs = logs[med.Id]
                .Where(l => l.ScheduledDate.Date >= start && l.ScheduledDate.Date <= end)
                .ToList();
            var bySlot = new Dictionary<(DateTime Date, TimeSpan Time), MedicationDoseLog>();
            foreach (var log in medLogs)
                bySlot[(log.ScheduledDate.Date, log.ScheduledTime)] = log;

            var slots = new HashSet<(DateTime Date, TimeSpan Time)>(bySlot.Keys);
            foreach (var s in schedules[med.Id])
                foreach (var occurrence in MedicationScheduleExpander
                             .Expand(s.Day, s.Time, start.AddTicks(-1), end.AddDays(1).AddTicks(-1)))
                    slots.Add((occurrence.Date, occurrence.TimeOfDay));

            foreach (var slot in slots)
            {
                bySlot.TryGetValue(slot, out var log);

                // A dose sits at the moment the owner tapped it, falling back to the
                // slot it was scheduled for — the same rule the Journal timeline and
                // Today's card already place doses by, so the bands agree with them.
                moments.Add(new RecordMoment(log?.ResolvedAt ?? (slot.Date + slot.Time), null));

                switch (log?.Status)
                {
                    case DoseStatus.Taken: given++; break;
                    case DoseStatus.Skipped: skipped++; break;

                    // Missed is what the reconciliation sweep writes; in the owner's
                    // language that is the same thing as a slot nobody answered, and
                    // neither is ever called "missed" out loud (AI/app-voice.md §9).
                    default: notRecorded++; break;
                }
            }
        }

        return RecordFactsBuilder.Build(kind, from, to, moments, NoMoments,
            new DoseCounts(given, skipped, notRecorded));
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private static readonly IEnumerable<RecordMoment> NoMoments = Array.Empty<RecordMoment>();

    /// <summary>Mood and weight store their time of day as nullable ticks; a row written
    /// before per-entry times has none and sits at the start of its day.</summary>
    private static DateTime At(DateTime date, long? ticks) =>
        date.Date + (ticks is long t ? TimeSpan.FromTicks(t) : TimeSpan.Zero);
}
