namespace Animal_Diary_App.Data.Services.Reports;

/// <summary>
/// A fully populated fake <see cref="VetReportData"/> so the PDF layout can be
/// iterated on WITHOUT real logged data on the device. Deterministic (seeded
/// random) so two runs produce the same document — layout diffs stay meaningful.
/// </summary>
public static class VetReportSampleData
{
    public static VetReportData Create()
    {
        var to = DateTime.Today;
        var from = to.AddDays(-90);
        var rng = new Random(42);

        // Weight: slow decline 31.2 → 29.8 kg, one reading roughly every 4 days.
        var weight = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 4)
            weight.Add(new ReportPoint(from.AddDays(d), 31.2m - 1.4m * d / 90m + (decimal)(rng.NextDouble() * 0.3 - 0.15)));

        // Glucose: 2 readings/day hovering around 9 mmol/L, only some days logged.
        var glucose = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 2)
            if (rng.NextDouble() > 0.25)
                glucose.Add(new ReportPoint(from.AddDays(d), 9m + (decimal)(rng.NextDouble() * 5 - 2.5)));

        // Water — two DISTINCT types (never merged): measured mL daily totals on some
        // days, and relative owner observations (1..5) on others. They overlap in time
        // on purpose, to show the two graphs are kept separate.
        var waterMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 3)
            if (rng.NextDouble() > 0.35)
                waterMeasured.Add(new ReportPoint(from.AddDays(d), 320m + (decimal)(rng.NextDouble() * 260)));
        var waterObservations = new List<ReportObservation>();
        for (var d = 1; d <= 90; d += 2)
            if (rng.NextDouble() > 0.4)
                waterObservations.Add(new ReportObservation(from.AddDays(d), rng.Next(1, 6)));

        // Appetite — measured grams on some days, qualitative observations on others,
        // and a small diet list. Same measured-vs-observed separation as water.
        var appetiteMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 3)
            if (rng.NextDouble() > 0.4)
                appetiteMeasured.Add(new ReportPoint(from.AddDays(d), 180m + (decimal)(rng.NextDouble() * 220)));
        var appetiteObservations = new List<ReportObservation>();
        for (var d = 1; d <= 90; d += 2)
            if (rng.NextDouble() > 0.35)
                appetiteObservations.Add(new ReportObservation(from.AddDays(d), rng.Next(1, 6)));
        var appetiteFoods = new[] { "Chicken kibble", "Wet food (salmon)", "Boiled rice + turkey" };

        // Seizures: a handful across the period.
        var seizureDays = new[] { 8, 9, 31, 55, 56, 80 };
        var events = new List<ReportEvent>();
        foreach (var d in seizureDays)
            events.Add(new ReportEvent
            {
                Kind = ReportEventKind.Seizure,
                Date = from.AddDays(d),
                Time = new TimeSpan(6 + rng.Next(14), rng.Next(60), 0),
                DurationMinutes = rng.Next(1, 5),
                Note = d == 31 ? "Disoriented for ~20 min afterwards, drank a lot" : null
            });
        events.Add(new ReportEvent { Kind = ReportEventKind.Vomiting, Date = from.AddDays(40) });
        events = events.OrderByDescending(e => e.Date).ThenByDescending(e => e.Time).ToList();

        // Seizures per week, derived the same way the real builder does it.
        var seizuresPerWeek = BuildWeeklyCounts(
            seizureDays.Select(d => from.AddDays(d)), from, to);

        return new VetReportData
        {
            Pet = new ReportPetInfo
            {
                Name = "Charly",
                Species = "Dog",
                AgeYears = 5,
                Conditions = new[] { "Diabetes", "Epilepsy / Seizures" },
                CurrentWeightKg = weight[^1].Value,
                WeightChangeKg = weight[^1].Value - weight[0].Value
            },
            From = from,
            To = to,
            GeneratedAt = DateTime.Now,
            Medications = new[]
            {
                new ReportMedication
                {
                    Name = "Caninsulin", Dose = 12, Unit = "IU",
                    DaysPerWeek = 7,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                    ScheduledCount = 180, TakenCount = 174, SkippedCount = 2, MissedCount = 4
                },
                new ReportMedication
                {
                    Name = "Phenobarbital", Dose = 60, Unit = "mg",
                    DaysPerWeek = 7,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                    ScheduledCount = 180, TakenCount = 179, SkippedCount = 0, MissedCount = 1
                },
                new ReportMedication
                {
                    Name = "Joint supplement", Dose = 1, Unit = "tab",
                    DaysPerWeek = 3,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0) },
                    ScheduledCount = 39, TakenCount = 31, SkippedCount = 5, MissedCount = 3
                }
            },
            Trends = new[]
            {
                new ReportSeries { Label = "Weight", Unit = "kg", Points = weight },
                new ReportSeries { Label = "Blood glucose", Unit = "mmol/L", Points = glucose },
                new ReportSeries { Label = "Seizures per week", Points = seizuresPerWeek }
            },
            Water = new ReportWater
            {
                Measured = new ReportSeries { Label = "Measured", Unit = "mL", Points = waterMeasured },
                Observations = waterObservations
            },
            Appetite = new ReportAppetite
            {
                Measured = new ReportSeries { Label = "Measured", Unit = "g", Points = appetiteMeasured },
                Observations = appetiteObservations,
                Foods = appetiteFoods
            },
            Events = events,
            Notes = new ReportNote[]
            {
                new(from.AddDays(82), "Started limping slightly on the left hind leg after long walks."),
                new(from.AddDays(60), "Is the panting at night normal with the new phenobarbital dose?"),
                new(from.AddDays(12), "Ate grass twice this week, seemed fine afterwards.")
            }
        };
    }

    /// <summary>Occurrences bucketed into calendar weeks (points dated at each
    /// week's start). Shared shape with the real builder's seizure series.</summary>
    internal static IReadOnlyList<ReportPoint> BuildWeeklyCounts(
        IEnumerable<DateTime> occurrences, DateTime from, DateTime to)
    {
        var dates = occurrences.Select(d => d.Date).ToList();
        var points = new List<ReportPoint>();
        for (var weekStart = from.Date; weekStart <= to.Date; weekStart = weekStart.AddDays(7))
        {
            var weekEnd = weekStart.AddDays(6);
            points.Add(new ReportPoint(weekStart, dates.Count(d => d >= weekStart && d <= weekEnd)));
        }
        return points;
    }
}
