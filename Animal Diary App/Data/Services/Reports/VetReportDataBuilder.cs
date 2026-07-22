namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// Builds the <see cref="VetReportData"/> snapshot for one pet + one date range by
/// querying the existing SQLite services. This is the ONLY report class that knows
/// where data lives; the document layer never sees a service or a model type.
/// </summary>
public class VetReportDataBuilder
{
    private readonly PetService _pets;
    private readonly PetConditionService _conditions;
    private readonly MedicationService _medications;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly SeizureEntryService _seizures;
    private readonly TrackingEntryService _tracking;
    private readonly TrackerService _trackers;

    public VetReportDataBuilder(
        PetService pets,
        PetConditionService conditions,
        MedicationService medications,
        MedicationDoseLogService doseLogs,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        SeizureEntryService seizures,
        TrackingEntryService tracking,
        TrackerService trackers)
    {
        _pets = pets;
        _conditions = conditions;
        _medications = medications;
        _doseLogs = doseLogs;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _seizures = seizures;
        _tracking = tracking;
        _trackers = trackers;
    }

    /// <summary>Snapshot everything the report might show for the pet in
    /// [<paramref name="from"/> .. <paramref name="to"/>] (inclusive, date-only).</summary>
    public async Task<VetReportData> BuildAsync(int petId, DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;

        var pet = await _pets.GetPetByIdAsync(petId)
            ?? throw new InvalidOperationException($"Pet {petId} not found.");

        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        var petEntries = await _petEntries.GetPetEntriesByPetIdAndRangeAsync(petId, from, to);
        var glucoseEntries = await _glucose.GetForRangeAsync(petId, from, to);
        var appetiteEntries = await _appetite.GetForRangeAsync(petId, from, to);
        var seizureEntries = await _seizures.GetForRangeAsync(petId, from, to);
        var trackingEntries = await _tracking.GetForRangeAsync(petId, from, to);

        var weightPoints = petEntries
            .Where(e => e.Weight > 0)
            .OrderBy(e => e.Date)
            .Select(e => new ReportPoint(e.Date, e.Weight))
            .ToList();

        var events = BuildEvents(seizureEntries, appetiteEntries, trackingEntries);

        return new VetReportData
        {
            Pet = await BuildPetInfoAsync(pet, conditionIds, weightPoints),
            From = from,
            To = to,
            GeneratedAt = DateTime.Now,
            Medications = await BuildMedicationsAsync(petId, from, to),
            Trends = await BuildTrendsAsync(petId, weightPoints, glucoseEntries, events, from, to),
            Events = events,
            // Only notes the owner explicitly opted into appear here; every other
            // note stays stored but private. Legacy entries default to false.
            Notes = petEntries
                .Where(e => e.IncludeInVetReport && !string.IsNullOrWhiteSpace(e.MoodNote))
                .OrderByDescending(e => e.Date)
                .Select(e => new ReportNote(e.Date, e.MoodNote.Trim()))
                .ToList()
        };
    }

    private async Task<ReportPetInfo> BuildPetInfoAsync(
        Pet pet, IReadOnlyList<string> conditionIds, List<ReportPoint> weightPoints)
    {
        // Current weight: last reading in range, else the pet's latest ever (so the
        // header still identifies the pet). Change is only stated when the range
        // itself contains at least two readings — never inferred across gaps.
        decimal? currentWeight = weightPoints.Count > 0 ? weightPoints[^1].Value : null;
        if (currentWeight == null)
            currentWeight = (await _petEntries.GetLatestWeightEntryAsync(pet.Id))?.Weight;

        return new ReportPetInfo
        {
            Name = pet.Name,
            Species = PetTypeNames.Localize(pet.Type),
            AgeYears = pet.AgeYears,
            Conditions = conditionIds
                .Select(id => ConditionCatalog.GetCondition(id).Name)
                .ToList(),
            CurrentWeightKg = currentWeight,
            WeightChangeKg = weightPoints.Count >= 2
                ? weightPoints[^1].Value - weightPoints[0].Value
                : null
        };
    }

    private async Task<List<ReportMedication>> BuildMedicationsAsync(int petId, DateTime from, DateTime to)
    {
        // Archived medications are included on purpose when they were still dosed in
        // the period — a vet reading 90 days of history needs the whole picture.
        var meds = await _medications.GetMedicationsByPetIdAsync(petId);
        var medIds = meds.Select(m => m.Id).ToList();
        var schedules = (await _medications.GetSchedulesForMedicationsAsync(medIds))
            .ToLookup(s => s.MedicationId);
        var logs = (await _doseLogs.GetByMedicationsAndRangeAsync(medIds, from, to))
            .ToLookup(l => l.MedicationId);

        var result = new List<ReportMedication>();
        foreach (var med in meds)
        {
            var medSchedules = schedules[med.Id].ToList();
            var medLogs = logs[med.Id].ToList();

            // Scheduled doses that actually fell inside the period. The schedule rows
            // only describe the CURRENT rules — an edited schedule would silently
            // rewrite history — so the count is the UNION of (a) the current rules
            // walked over the period and (b) every dose log in the period (each log
            // row proves a dose was scheduled then, whatever the rules said at the
            // time). Bounded below by the medication's creation date and above by
            // today (future doses aren't "scheduled yet" for adherence purposes).
            var start = med.CreatedAt.Date > from ? med.CreatedAt.Date : from;
            var end = to < DateTime.Today ? to : DateTime.Today;

            var scheduledKeys = new HashSet<(DateTime Date, TimeSpan Time)>();
            foreach (var s in medSchedules)
                foreach (var occurrence in Animal_Diary_App.Data.Services.Notifications.MedicationScheduleExpander
                             .Expand(s.Day, s.Time, start.AddTicks(-1), end.AddDays(1).AddTicks(-1)))
                    scheduledKeys.Add((occurrence.Date, occurrence.TimeOfDay));
            foreach (var log in medLogs.Where(l => l.ScheduledDate.Date >= start && l.ScheduledDate.Date <= end))
                scheduledKeys.Add((log.ScheduledDate.Date, log.ScheduledTime));
            var scheduled = scheduledKeys.Count;

            // Nothing scheduled and nothing logged in the period → the medication
            // played no role in it; leave it out entirely.
            if (scheduled == 0 && medLogs.Count == 0)
                continue;

            result.Add(new ReportMedication
            {
                Name = med.Name,
                Dose = med.Dosage,
                Unit = med.Unit,
                DaysPerWeek = medSchedules.Select(s => s.Day).Distinct().Count(),
                TimesOfDay = medSchedules.Select(s => s.Time).Distinct().OrderBy(t => t).ToList(),
                ScheduledCount = scheduled,
                TakenCount = medLogs.Count(l => l.Status == DoseStatus.Taken),
                SkippedCount = medLogs.Count(l => l.Status == DoseStatus.Skipped),
                MissedCount = medLogs.Count(l => l.Status == DoseStatus.Missed)
            });
        }
        return result;
    }

    private async Task<List<ReportSeries>> BuildTrendsAsync(
        int petId,
        List<ReportPoint> weightPoints,
        List<GlucoseEntry> glucoseEntries,
        List<ReportEvent> events,
        DateTime from,
        DateTime to)
    {
        var trends = new List<ReportSeries>();

        if (weightPoints.Count >= 2)
            trends.Add(new ReportSeries { Label = "Weight", Unit = "kg", Points = weightPoints });

        if (glucoseEntries.Count >= 2)
        {
            // The unit lives on the pet's glucose tracker ("mmol/L" today).
            var tracker = await _trackers.GetByTrackerIdAsync(petId, TrackerId.Glucose);
            trends.Add(new ReportSeries
            {
                Label = "Blood glucose",
                Unit = string.IsNullOrEmpty(tracker?.Unit) ? "mmol/L" : tracker!.Unit,
                Points = glucoseEntries
                    .OrderBy(g => g.Date).ThenBy(g => g.Time)
                    .Select(g => new ReportPoint(g.Date + g.Time, g.Value))
                    .ToList()
            });
        }

        var seizureDates = events.Where(e => e.Kind == ReportEventKind.Seizure).Select(e => e.Date).ToList();
        if (seizureDates.Count > 0)
            trends.Add(new ReportSeries
            {
                Label = "Seizures per week",
                Points = VetReportSampleData.BuildWeeklyCounts(seizureDates, from, to)
            });

        return trends;
    }

    private static List<ReportEvent> BuildEvents(
        List<SeizureEntry> seizureEntries,
        List<AppetiteEntry> appetiteEntries,
        List<TrackingEntry> trackingEntries)
    {
        var events = new List<ReportEvent>();

        // Seizures live in the typed Journal store…
        events.AddRange(seizureEntries.Select(s => new ReportEvent
        {
            Kind = ReportEventKind.Seizure,
            Date = s.Date,
            Time = s.Time,
            DurationMinutes = s.DurationMinutes,
            Note = string.IsNullOrWhiteSpace(s.Note) ? null : s.Note.Trim()
        }));

        // …but the Calendar's older dynamic tracker wrote them to TrackingEntry, so
        // both stores are read until that data is migrated.
        events.AddRange(trackingEntries
            .Where(t => t.ItemId == "seizure" && (t.Flag == true || t.DurationSeconds.HasValue || t.TimeTicks.HasValue))
            .Select(t => new ReportEvent
            {
                Kind = ReportEventKind.Seizure,
                Date = t.Date,
                Time = t.TimeTicks.HasValue ? new TimeSpan(t.TimeTicks.Value) : null,
                DurationMinutes = t.DurationSeconds.HasValue
                    ? (int)Math.Round(t.DurationSeconds.Value / 60.0, MidpointRounding.AwayFromZero)
                    : null,
                Note = string.IsNullOrWhiteSpace(t.Text) ? null : t.Text!.Trim()
            }));

        events.AddRange(trackingEntries
            .Where(t => t.ItemId == "vomiting" && t.Flag == true)
            .Select(t => new ReportEvent { Kind = ReportEventKind.Vomiting, Date = t.Date }));

        // "Ate nothing / barely anything" readings, reported as the owner's own
        // logged level — a fact, not an assessment.
        events.AddRange(appetiteEntries
            .Where(a => a.Level <= (int)AppetiteLevel.Barely)
            .Select(a => new ReportEvent
            {
                Kind = ReportEventKind.LowAppetite,
                Date = a.Date,
                Time = a.Time,
                Value = a.Level
            }));

        return events
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Time ?? TimeSpan.Zero)
            .ToList();
    }
}
