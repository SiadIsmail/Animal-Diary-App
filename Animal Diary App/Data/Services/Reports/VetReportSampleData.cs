namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Reports.Document;
using Animal_Diary_App.Helpers;

/// <summary>
/// Fake <see cref="VetReportData"/> so the PDF can be generated WITHOUT real logged
/// data on the device. Both fixtures are deterministic (seeded random) so two runs
/// produce the same document — layout diffs stay meaningful.
///
/// Two fixtures, for two different jobs:
/// <list type="bullet">
/// <item><see cref="Create"/> — fully populated, 90 days, every section on. Runs to
///   two pages on purpose: this is the one that exercises page breaks, the events
///   cap and the continuation header. Use it when changing the document layer.</item>
/// <item><see cref="CreateCompact"/> — trimmed to a single page for store
///   screenshots. See its own note on what was left out and why.</item>
/// </list>
/// </summary>
public static class VetReportSampleData
{
    /// <summary>The full fixture — 90 days, every section populated, two pages.
    /// The layout-stress case; see the class note.</summary>
    public static VetReportData Create()
    {
        var to = DateTime.Today;
        var from = to.AddDays(-90);
        var rng = new Random(42);

        // Weight: slow decline 5.9 → 5.2 kg, one reading roughly every 4 days. Cat-sized
        // numbers, so the jitter is cat-sized too — ±60 g, not ±150 g.
        var weight = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 4)
            weight.Add(new ReportPoint(from.AddDays(d), 5.9m - 0.7m * d / 90m + (decimal)(rng.NextDouble() * 0.12 - 0.06)));

        // Glucose: spot readings around 12 mmol/L, only some days logged — a cat on
        // twice-daily insulin swings wider than the old dog sample did.
        var glucose = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 2)
            if (rng.NextDouble() > 0.25)
                glucose.Add(new ReportPoint(from.AddDays(d), 12m + (decimal)(rng.NextDouble() * 10 - 5)));

        // Water — two DISTINCT types (never merged): measured mL daily totals on some
        // days, and relative owner observations (1..5) on others. They overlap in time
        // on purpose, to show the two graphs are kept separate.
        var waterMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 3)
            if (rng.NextDouble() > 0.35)
                waterMeasured.Add(new ReportPoint(from.AddDays(d), 240m + (decimal)(rng.NextDouble() * 220)));
        var waterObservations = new List<ReportObservation>();
        for (var d = 1; d <= 90; d += 2)
            if (rng.NextDouble() > 0.4)
                waterObservations.Add(new ReportObservation(from.AddDays(d), rng.Next(1, 6)));

        // Appetite — measured grams on some days, qualitative observations on others,
        // and a small diet list. Same measured-vs-observed separation as water.
        var appetiteMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 90; d += 3)
            if (rng.NextDouble() > 0.4)
                appetiteMeasured.Add(new ReportPoint(from.AddDays(d), 120m + (decimal)(rng.NextDouble() * 110)));
        var appetiteObservations = new List<ReportObservation>();
        for (var d = 1; d <= 90; d += 2)
            if (rng.NextDouble() > 0.35)
                appetiteObservations.Add(new ReportObservation(from.AddDays(d), rng.Next(1, 6)));
        var appetiteFoods = new[] { "Wet food (chicken pâté)", "Low-carb diabetic wet food", "Freeze-dried chicken treats" };

        // Mood — most days but not all, so the layout harness shows how the chart reads
        // with gaps in it (an unlogged day must not look like a bad one).
        var moodObservations = new List<ReportObservation>();
        for (var d = 0; d <= 90; d++)
            if (rng.NextDouble() > 0.25)
                moodObservations.Add(new ReportObservation(from.AddDays(d), rng.Next(1, 6)));

        // Events: what a diabetic cat's owner ends up reporting — a few vomits and the
        // days appetite fell away. Two kinds on purpose, one timed and one not, so the
        // table is exercised both with a time and with the "—" placeholder.
        var events = new List<ReportEvent>();
        foreach (var d in new[] { 12, 41, 63 })
            events.Add(new ReportEvent
            {
                Kind = ReportEventKind.Vomiting,
                Date = from.AddDays(d),
                Time = new TimeSpan(6 + rng.Next(14), rng.Next(60), 0),
                Note = d == 41 ? "Brought her breakfast back up about an hour after the morning insulin" : null
            });
        foreach (var (d, level) in new[] { (40, 2), (62, 1), (78, 2) })
            events.Add(new ReportEvent
            {
                Kind = ReportEventKind.LowAppetite,
                Date = from.AddDays(d),
                Value = level
            });
        events = events.OrderByDescending(e => e.Date).ThenByDescending(e => e.Time).ToList();

        return new VetReportData
        {
            Pet = new ReportPetInfo
            {
                Name = "Luna",
                // Resolved through the same helpers the real builder uses, so a sample
                // rendered in German says "Katze", not "Cat" — these reach the page.
                Species = PetTypeNames.Localize("Cat"),
                AgeYears = 9,
                Conditions = new[] { ConditionCatalog.GetCondition("diabetes").Name },
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
                    Name = "ProZinc (insulin)", Dose = 2, Unit = "IU",
                    DaysPerWeek = 7,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                    ScheduledCount = 180, TakenCount = 174, SkippedCount = 2, MissedCount = 4
                },
                new ReportMedication
                {
                    Name = "Mirtazapine", Dose = 1.88m, Unit = "mg",
                    DaysPerWeek = 3,
                    TimesOfDay = new[] { new TimeSpan(20, 0, 0) },
                    ScheduledCount = 39, TakenCount = 31, SkippedCount = 5, MissedCount = 3
                },
                new ReportMedication
                {
                    Name = "Omega-3 supplement", Dose = 1, Unit = "cap",
                    DaysPerWeek = 7,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0) },
                    ScheduledCount = 90, TakenCount = 84, SkippedCount = 3, MissedCount = 3
                }
            },
            Trends = new[]
            {
                new ReportSeries { Label = VetReportStrings.SeriesWeight, Unit = "kg", Points = weight },
                new ReportSeries { Label = VetReportStrings.SeriesGlucose, Unit = "mmol/L", Points = glucose }
            },
            Water = new ReportWater
            {
                Measured = new ReportSeries { Label = VetReportStrings.Measured, Unit = "mL", Points = waterMeasured },
                Observations = waterObservations
            },
            Appetite = new ReportAppetite
            {
                Measured = new ReportSeries { Label = VetReportStrings.Measured, Unit = "g", Points = appetiteMeasured },
                Observations = appetiteObservations,
                Foods = appetiteFoods
            },
            Mood = new ReportMood { Observations = moodObservations },
            Events = events,
            // An owner-defined pair, so the layout can be checked with the two shapes it
            // has to render: a Tick (vomiting — a bare occurrence with the owner's note)
            // and an Amount with their own unit (walks). "Walk" is here on purpose even
            // though it is the archetypal NON-clinical tracker: the fixture's job is to
            // exercise the layout, and a household that walks a limping dog would turn it
            // on. Only trackers whose switch is on ever reach this object.
            Custom = new ReportCustom
            {
                Trackers = new ReportCustomTracker[]
                {
                    new("Vomiting", string.Empty, 3),
                    new("Walk", "min", 4),
                },
                Entries = new ReportCustomEntry[]
                {
                    new("Walk", "min", from.AddDays(84), new TimeSpan(17, 40, 0), 25, "Slower than usual, wanted to turn back"),
                    new("Vomiting", string.Empty, from.AddDays(83), new TimeSpan(6, 15, 0), null, "Undigested food, about an hour after breakfast"),
                    new("Walk", "min", from.AddDays(83), new TimeSpan(8, 5, 0), 40, null),
                    new("Vomiting", string.Empty, from.AddDays(70), new TimeSpan(22, 30, 0), null, null),
                    new("Walk", "min", from.AddDays(70), new TimeSpan(18, 0, 0), 35, null),
                    new("Vomiting", string.Empty, from.AddDays(41), new TimeSpan(9, 0, 0), null, "Bile only"),
                    new("Walk", "min", from.AddDays(41), new TimeSpan(7, 50, 0), 30, null),
                },
            },
            Notes = new ReportNote[]
            {
                new(from.AddDays(82), "Litter tray is soaked most mornings — changing it twice a day now."),
                new(from.AddDays(60), "Is it normal that she still asks for food an hour after her evening insulin?"),
                new(from.AddDays(12), "Jumped onto the windowsill again for the first time in weeks.")
            }
        };
    }

    /// <summary>
    /// A deliberately SMALLER Luna, sized to land on exactly one page — the fixture for
    /// app-store screenshots, where a screenshot of page 1 of 2 would be cut off mid-report.
    ///
    /// One page is bought by carrying LESS, never by shrinking type or charts: the
    /// document's own styles are untouched, so what a store image shows is exactly what
    /// an owner gets. Three levers, in the order they were pulled:
    /// <list type="bullet">
    /// <item><b>30 days instead of 90</b> — the charts are fixed-height either way, but a
    ///   month of points reads as a legible line at screenshot size where a quarter reads
    ///   as a scribble.</item>
    /// <item><b>Appetite, events and mood left out</b> — the costliest blocks (~240 pt, a
    ///   whole table, and ~130 pt). Every section omits itself when empty, so nothing
    ///   looks missing.</item>
    /// <item><b>Two medications, worded short</b> — a third row, or an adherence string long
    ///   enough to wrap, is what pushes the table from tidy to tall.</item>
    /// </list>
    /// The mood chart's ~130 pt went to the notes list instead: a run of dots on a word
    /// axis needs the reader to decode it, while six dated sentences in the owner's own
    /// voice are legible at thumbnail size and say what the app is for. Mood is still in
    /// the real report — this is a screenshot's priorities, not the product's.
    /// </summary>
    public static VetReportData CreateCompact()
    {
        var to = DateTime.Today;
        var from = to.AddDays(-30);
        var rng = new Random(7);

        // Weight: 5.4 → 5.2 kg over the month, one weigh-in every 4 days. Few enough
        // points to read as a line rather than a comb at screenshot size.
        var weight = new List<ReportPoint>();
        for (var d = 0; d <= 30; d += 4)
            weight.Add(new ReportPoint(from.AddDays(d), 5.4m - 0.2m * d / 30m + (decimal)(rng.NextDouble() * 0.08 - 0.04)));

        // Glucose: one reading most days, settling 13 → 10 mmol/L across the month with a
        // small daily wobble. Calmer than the 90-day fixture on purpose — a screenshot has
        // to be legible at thumbnail size, and 30 points is the most that stays readable.
        var glucose = new List<ReportPoint>();
        for (var d = 0; d <= 30; d++)
            if (rng.NextDouble() > 0.15)
                glucose.Add(new ReportPoint(from.AddDays(d), 13m - 3m * d / 30m + (decimal)(rng.NextDouble() * 2.4 - 1.2)));

        // Water: measured millilitres only. Observations are dropped here purely for
        // height — one chart instead of two — not because the pairing matters less.
        var waterMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 30; d += 2)
            waterMeasured.Add(new ReportPoint(from.AddDays(d), 260m + (decimal)(rng.NextDouble() * 120)));

        return new VetReportData
        {
            Pet = new ReportPetInfo
            {
                Name = "Luna",
                // Resolved through the same helpers the real builder uses, so a sample
                // rendered in German says "Katze", not "Cat" — these reach the page.
                Species = PetTypeNames.Localize("Cat"),
                AgeYears = 9,
                Conditions = new[] { ConditionCatalog.GetCondition("diabetes").Name },
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
                    Name = "ProZinc (insulin)", Dose = 2, Unit = "IU",
                    DaysPerWeek = 7,
                    TimesOfDay = new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                    ScheduledCount = 60, TakenCount = 60
                },
                new ReportMedication
                {
                    Name = "Mirtazapine", Dose = 1.88m, Unit = "mg",
                    DaysPerWeek = 3,
                    TimesOfDay = new[] { new TimeSpan(20, 0, 0) },
                    ScheduledCount = 13, TakenCount = 12, MissedCount = 1
                }
            },
            Trends = new[]
            {
                new ReportSeries { Label = VetReportStrings.SeriesWeight, Unit = "kg", Points = weight },
                new ReportSeries { Label = VetReportStrings.SeriesGlucose, Unit = "mmol/L", Points = glucose }
            },
            Water = new ReportWater
            {
                Measured = new ReportSeries { Label = "Measured", Unit = "mL", Points = waterMeasured }
            },
            // The section the mood chart made room for: ten notes, which is exactly
            // VetReportStyles.MaxNotes, so the list is full without the "+N earlier"
            // line. Each is kept short enough to stay on one line — a wrapped note
            // costs a whole row and the German set runs longer than the English.
            Notes = SampleNotes(from)
        };
    }

    /// <summary>
    /// The compact fixture's owner notes, in the app's current language.
    ///
    /// Real owner notes are stored text and are NEVER translated — the report prints
    /// them exactly as they were typed, and that rule is not bent here. These are not
    /// owner notes; they are fake copy standing in for them, and a German store
    /// screenshot showing English sentences would misrepresent the product. Kept in
    /// this file rather than AppStrings because it is fixture copy, not app copy.
    /// </summary>
    private static ReportNote[] SampleNotes(DateTime from)
    {
        var german = LocalizationManager.Instance.CurrentLanguage == "de";

        var text = german
            ? new[]
            {
                "Die Morgenwerte sind ruhiger, seit wir das Insulin eine halbe Stunde später geben.",
                "Hat den Napf vor dem Frühstück wieder ganz leer getrunken.",
                "Beim Tierarzt: Gewicht passt, Dosis bleibt wie sie ist.",
                "Abends die Hälfte stehen gelassen, später doch noch aufgefressen.",
                "Liegt nachmittags wieder auf der Fensterbank.",
                "Hat durchgeschlafen, ohne nach Futter zu fragen.",
                "Trinkt seit dem Wochenende sichtbar weniger.",
                "Zweite Spritze pünktlich um 20:00 gegeben.",
                "Fell sieht wieder glatter aus.",
                "Spielt abends von selbst mit der Schnur."
            }
            : new[]
            {
                "Morning readings have settled since we moved her insulin half an hour later.",
                "Drank the whole bowl before breakfast again.",
                "Vet visit: happy with her weight, keep the dose as it is.",
                "Left half her dinner, ate it later in the evening.",
                "Back on the windowsill in the afternoons.",
                "Slept through the night without asking to be fed.",
                "Drinking noticeably less since the weekend.",
                "Evening injection given on time at 20:00.",
                "Her coat is looking smoother again.",
                "Started playing with the string on her own in the evenings."
            };

        // Newest first, spread across the month — the order NotesSection expects.
        var days = new[] { 28, 25, 22, 19, 16, 13, 10, 8, 5, 2 };
        return text.Select((t, i) => new ReportNote(from.AddDays(days[i]), t)).ToArray();
    }

    /// <summary>Occurrences bucketed into calendar weeks (points dated at each
    /// week's start). The sample pet has no seizures, but the real builder still
    /// derives its seizures-per-week series with this.</summary>
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
