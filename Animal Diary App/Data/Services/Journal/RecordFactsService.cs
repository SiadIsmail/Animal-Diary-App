namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Notifications;

// ─────────────────────────────────────────────────────────────────────────────
//  What the owner wrote down about one record, over one stretch of time.
//
//  A READ MODEL, not a screen. It introduces no table, stores nothing, and reads
//  the entry stores that already own each record: the same posture as
//  ConstellationService and TodayCardService.
//
//  Being one service consumed by three surfaces (Today's card sheet, the
//  Constellation legend, the appointment summary) is what makes "computed
//  identically for every kind" true by construction. A second copy of this
//  arithmetic anywhere is how a statement that only appears for seizures, or only
//  when the numbers are interesting, gets in, and that is exactly the line this
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
    /// </summary>
    public async Task<RecordFacts> GetAsync(Pet? pet, TodayCardKey kind, DateTime from, DateTime to)
        => (await GetSnapshotAsync(pet, kind, from, to)).Facts;

    /// <summary>
    /// The facts <b>and</b> the marks a surface can draw, from ONE pass over the store.
    ///
    /// <para>The two were always the same read: the facts path was already gathering
    /// exactly these moments and handing them to the builder. A caller that wants both
    /// (Today's look-back section) would otherwise scan every record twice.</para>
    ///
    /// <para>Never throws: a record whose store cannot be read yields the same
    /// zero-count snapshot as one that is genuinely empty, because the surfaces that
    /// show this have nothing useful to say about the difference.</para>
    /// </summary>
    public async Task<RecordSnapshot> GetSnapshotAsync(Pet? pet, TodayCardKey kind, DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;

        if (pet is null || pet.Id == 0)
            return RecordSnapshot.Empty(RecordFacts.Empty(kind, from, to));

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
                _ => RecordSnapshot.Empty(RecordFacts.Empty(kind, from, to)),
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RecordFacts] {kind} failed: {ex.Message}");
            return RecordSnapshot.Empty(RecordFacts.Empty(kind, from, to));
        }
    }

    // ── One-per-day stores: mood and weight share the day's PetEntry row ──────
    //
    // Both count DAYS RECORDED, because that is what the store holds: one row per
    // pet per day, re-logging replaces it. A legacy row with no recorded time sits at
    // the start of its day, exactly as the Constellation places it: it still happened.

    private async Task<RecordSnapshot> WeightAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);
        var measured = rows.Where(e => e.Weight > 0)
            .Select(e => new RecordMoment(At(e.Date, e.WeightTimeTicks), e.Weight))
            .ToList();

        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to, NoMoments, measured),
            measured, NoObservations, NoMoments);
    }

    private async Task<RecordSnapshot> MoodAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var all = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);
        var rows = all.Where(e => e.MoodLevel > 0).ToList();

        // No value: a mood is a word (see MoodLevel). Passing the stored 1–5 into a
        // RecordMoment would hand "Lowest 1 · Highest 4" to a surface that must never
        // turn an observation into a number (AI/design-decisions.md, "Communication
        // layer"), which is why the level travels in a RecordObservation instead, where
        // nothing can take its minimum.
        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to,
                NoMoments,
                rows.Select(e => new RecordMoment(At(e.Date, e.MoodTimeTicks), null))),
            NoMoments,
            rows.Select(e => new RecordObservation(At(e.Date, e.MoodTimeTicks), e.MoodLevel)).ToList(),
            NoMoments);
    }

    // ── Event stores ─────────────────────────────────────────────────────────

    private async Task<RecordSnapshot> GlucoseAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _glucose.GetForRangeAsync(petId, from, to);
        var measured = rows.Select(g => new RecordMoment(g.Date.Date + g.Time, g.Value)).ToList();

        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to, measured, NoMoments),
            measured, NoObservations, NoMoments);
    }

    private async Task<RecordSnapshot> SeizureAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = await _seizures.GetForRangeAsync(petId, from, to);

        // Duration is deliberately NOT the value. It is an attribute of the occurrence,
        // not the reading, and "Lowest 1 · Highest 6" minutes reads as a severity scale
        // the app has no business implying. So a seizure carries no number at all, and
        // the only thing a surface can draw is WHEN it happened.
        var events = rows.Select(s => new RecordMoment(s.Date.Date + s.Time, null)).ToList();

        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to, events, NoMoments),
            NoMoments, NoObservations, events);
    }

    // ── The two-store records ────────────────────────────────────────────────
    //
    // Water and appetite each have a MEASURED store (additive events: mL, grams) and
    // an OBSERVED store (one relative word per day). The two are never merged into a
    // value or a chart, and an observation is never converted to a number: only the
    // measured side carries a Value here, so Lowest/Highest can only ever be mL or
    // grams. What IS combined is the COUNT, and only because a count is a fact about
    // the diary ("you wrote something down about water 40 times"), not about the
    // animal, and is reached the same way for every kind.

    private async Task<RecordSnapshot> AppetiteAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var amounts = await _appetite.GetAmountsForRangeAsync(petId, from, to);
        var levels = await _appetite.GetForRangeAsync(petId, from, to);

        var measured = amounts.Select(a => new RecordMoment(a.Date.Date + a.Time, a.Grams)).ToList();

        // TWO lists, never one. A surface holding both draws two separate charts; there
        // is no shape here that could accidentally merge grams with "ate most of it".
        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to,
                measured,
                levels.Select(l => new RecordMoment(l.Date.Date + l.Time, null))),
            measured,
            levels.Select(l => new RecordObservation(l.Date.Date + l.Time, l.Level)).ToList(),
            NoMoments);
    }

    private async Task<RecordSnapshot> WaterAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var amounts = await _water.GetAmountsForRangeAsync(petId, from, to);
        var levels = await _water.GetLevelsForRangeAsync(petId, from, to);

        var measured = amounts.Select(a => new RecordMoment(a.Date.Date + a.Time, a.AmountMl)).ToList();

        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to,
                measured,
                levels.Select(l => new RecordMoment(l.Date.Date + l.Time, null))),
            measured,
            levels.Select(l => new RecordObservation(l.Date.Date + l.Time, l.Level)).ToList(),
            NoMoments);
    }

    // ── Owner-defined trackers ───────────────────────────────────────────────

    private async Task<RecordSnapshot> CustomAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        var rows = (await _custom.GetForRangeAsync(petId, from, to))
            .Where(e => e.CustomTrackerId == kind.CustomId)
            .Select(e => new RecordMoment(e.Date.Date + e.Time, e.Amount))
            .ToList();

        // An Amount tracker's number is in the OWNER's unit, so lowest and highest are
        // theirs to read; a Tick tracker has no number at all and Amount is null, which
        // the builder simply leaves out. The split below is the same question asked of
        // the data rather than of the definition: a tracker the owner declared as
        // Amount but only ever ticked draws as marks, which is what it actually is.
        var measured = rows.Where(m => m.Value is not null).ToList();
        var events = rows.Where(m => m.Value is null).ToList();

        return new RecordSnapshot(
            RecordFactsBuilder.Build(kind, from, to, rows, NoMoments),
            measured, NoObservations, events);
    }

    // ── Doses ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pet's doses over the range: given, skipped, not recorded.
    ///
    /// <para>The dose slots are the UNION of (a) the current schedule rules walked over
    /// the period and (b) every dose log in it: the same rule the vet report follows,
    /// and for the same reason: schedule rows describe only the CURRENT rules, so
    /// counting from them alone lets an edit silently rewrite history, while each log
    /// row proves a dose was scheduled then. Bounded below by each medication's creation
    /// date and above by today, so a past day can never show phantom doses and a future
    /// one is not yet "not recorded".</para>
    /// </summary>
    private async Task<RecordSnapshot> MedicationAsync(int petId, TodayCardKey kind, DateTime from, DateTime to)
    {
        // Archived medications are included when they were still dosed in the period,
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
                // slot it was scheduled for: the same rule the Journal timeline and
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

        // Facts only, and no marks to draw. Three hundred doses over a quarter is a solid
        // band on any axis: it would say "this pet is medicated", which the three counts
        // below already say better.
        return RecordSnapshot.Empty(
            RecordFactsBuilder.Build(kind, from, to, moments, NoMoments,
                new DoseCounts(given, skipped, notRecorded)));
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private static readonly RecordMoment[] NoMoments = Array.Empty<RecordMoment>();
    private static readonly RecordObservation[] NoObservations = Array.Empty<RecordObservation>();

    /// <summary>Mood and weight store their time of day as nullable ticks; a row written
    /// before per-entry times has none and sits at the start of its day.</summary>
    private static DateTime At(DateTime date, long? ticks) =>
        date.Date + (ticks is long t ? TimeSpan.FromTicks(t) : TimeSpan.Zero);
}
