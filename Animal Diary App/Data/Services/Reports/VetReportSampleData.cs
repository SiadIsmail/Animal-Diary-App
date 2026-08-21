namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Reports.Document;
using Animal_Diary_App.Helpers;

/// <summary>
/// One fake <see cref="VetReportData"/>, for one job: a report that lands on exactly one
/// page, for app-store screenshots. Deterministic (seeded random), so two runs produce the
/// same document and layout diffs stay meaningful.
///
/// <para>There used to be a second, fully-populated 90-day fixture here for layout work.
/// The seeded demo pets replaced it: exporting Kira or Mira runs the REAL builder over
/// real rows, which exercises page breaks, the events cap and the continuation header more
/// honestly than a hand-written <see cref="VetReportData"/> could. This one survives
/// because a demo pet cannot do its job: a full year of a real diary never ends on page
/// one, and a store screenshot of "page 1 of 3" is cut off mid-report.</para>
/// </summary>
public static class VetReportSampleData
{
    /// <summary>
    /// A deliberately SMALLER Luna, sized to land on exactly one page: the fixture for
    /// app-store screenshots, where a screenshot of page 1 of 2 would be cut off mid-report.
    ///
    /// One page is bought by carrying LESS, never by shrinking type or charts: the
    /// document's own styles are untouched, so what a store image shows is exactly what
    /// an owner gets. Three levers, in the order they were pulled:
    /// <list type="bullet">
    /// <item><b>30 days instead of 90</b>: the charts are fixed-height either way, but a
    ///   month of points reads as a legible line at screenshot size where a quarter reads
    ///   as a scribble.</item>
    /// <item><b>Appetite, events and mood left out</b>: the costliest blocks (~240 pt, a
    ///   whole table, and ~130 pt). Every section omits itself when empty, so nothing
    ///   looks missing.</item>
    /// <item><b>Two medications, worded short</b>: a third row, or an adherence string long
    ///   enough to wrap, is what pushes the table from tidy to tall.</item>
    /// </list>
    /// The mood chart's ~130 pt went to the notes list instead: a run of dots on a word
    /// axis needs the reader to decode it, while six dated sentences in the owner's own
    /// voice are legible at thumbnail size and say what the app is for. Mood is still in
    /// the real report: this is a screenshot's priorities, not the product's.
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
        // small daily wobble. Calmer than the 90-day fixture on purpose: a screenshot has
        // to be legible at thumbnail size, and 30 points is the most that stays readable.
        var glucose = new List<ReportPoint>();
        for (var d = 0; d <= 30; d++)
            if (rng.NextDouble() > 0.15)
                glucose.Add(new ReportPoint(from.AddDays(d), 13m - 3m * d / 30m + (decimal)(rng.NextDouble() * 2.4 - 1.2)));

        // Water: measured millilitres only. Observations are dropped here purely for
        // height (one chart instead of two) not because the pairing matters less.
        var waterMeasured = new List<ReportPoint>();
        for (var d = 0; d <= 30; d += 2)
            waterMeasured.Add(new ReportPoint(from.AddDays(d), 260m + (decimal)(rng.NextDouble() * 120)));

        return new VetReportData
        {
            Pet = new ReportPetInfo
            {
                Name = "Luna",
                // Resolved through the same helpers the real builder uses, so a sample
                // rendered in German says "Katze", not "Cat": these reach the page.
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
            // line. Each is kept short enough to stay on one line: a wrapped note
            // costs a whole row and the German set runs longer than the English.
            Notes = SampleNotes(from)
        };
    }

    /// <summary>
    /// The compact fixture's owner notes, in the app's current language.
    ///
    /// Real owner notes are stored text and are NEVER translated: the report prints
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

        // Newest first, spread across the month: the order NotesSection expects.
        var days = new[] { 28, 25, 22, 19, 16, 13, 10, 8, 5, 2 };
        return text.Select((t, i) => new ReportNote(from.AddDays(days[i]), t)).ToArray();
    }

}
