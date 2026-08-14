namespace Animal_Diary_App.Data.Services.Journal;

using System.Globalization;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Helpers;

/// <summary>
/// The Constellation's development switch: fill the sky with a made-up care history
/// so the density behaviour can be looked at without logging for a year.
///
/// <para>Same shape and same reason as <c>VetReportSampleData</c> — the thing being
/// worked on is a LAYOUT, and a layout that only appears after months of real use
/// cannot be iterated on. This one exists because the whole point of the sky is what
/// it does at 5 entries, at 500 and at 5,000, and only one of those is reachable on a
/// dev device.</para>
/// </summary>
public static class ConstellationConfig
{
    /// <summary>
    /// <b>Flip this to see a full sky. Flip it back before shipping.</b>
    ///
    /// <para>When true, the Constellation ignores the database entirely and draws
    /// <see cref="ConstellationSampleSky"/> instead. Nothing is written — the fixture
    /// is a read model built in memory, so it cannot reach an entry store, a sync
    /// push, or a vet report — and the page wears an unmissable SAMPLE DATA banner
    /// while it is on, because invented medical history shown as an owner's own
    /// records is the worst thing this app could do.</para>
    ///
    /// <para>A compile-time <c>const</c> on purpose, like <c>AnalyticsConfig.Enabled</c>
    /// and the billing switches: the unreachable branch is the point (CS0162 is
    /// suppressed in the csproj for exactly these), and a runtime toggle would be one
    /// more thing that could end up on in someone's hands.</para>
    /// </summary>
    public const bool UseSampleSky = false;
}

/// <summary>
/// A year of a dog with epilepsy who is also on twice-daily medication, whose owner
/// logs most days and some days not at all. Deterministic (seeded), so the same sky
/// is drawn on every reload and a layout change is the only thing that moves.
///
/// <para>It is built to exercise the cases that actually break the drawing:</para>
/// <list type="bullet">
/// <item><b>All eight symbols</b>, in the proportions a real diary has — hundreds of
///   doses, a handful of seizures.</item>
/// <item><b>Clusters</b> — a glucose curve is three readings in six hours, a seizure
///   day is two or three inside an hour. This is what the fan is for.</item>
/// <item><b>Gaps</b> — a fortnight in the middle where nothing was written down.
///   Empty stretches are normal, and they must look calm rather than broken.</item>
/// <item><b>Scale</b> — roughly ten a day, so the 1-year range lands near 3,500 and
///   the 1-week range near 70. Both have to read.</item>
/// </list>
/// </summary>
public static class ConstellationSampleSky
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>The gap: a fortnight, this far back from the end of the range, where
    /// the owner wrote nothing down at all.</summary>
    private const int QuietSpellStartDaysAgo = 96;
    private const int QuietSpellLength = 14;

    public static List<CelestialEvent> Generate(DateTime from, DateTime to)
    {
        // Seeded from the range rather than the clock: the same stretch redraws
        // identically, and switching 30 days to 90 days extends the sky instead of
        // inventing a different one.
        var rng = new Random(unchecked((int)(to.Date.Ticks / TimeSpan.TicksPerDay)));
        var events = new List<CelestialEvent>();

        var quietFrom = to.Date.AddDays(-QuietSpellStartDaysAgo);
        var quietTo = quietFrom.AddDays(QuietSpellLength);

        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (day >= quietFrom && day < quietTo)
                continue;

            // Some days nobody manages to write anything down. That is normal, and the
            // sky must not make it look like an outage.
            if (rng.NextDouble() < 0.06)
                continue;

            AddDoses(events, rng, day);
            AddMood(events, rng, day);
            AddWeight(events, rng, day);
            AddGlucose(events, rng, day);
            AddAppetite(events, rng, day);
            AddWater(events, rng, day);
            AddSeizures(events, rng, day);
            AddCustom(events, rng, day);
        }

        events.Sort((a, b) => a.When.CompareTo(b.When));
        return events;
    }

    // Two a day, given within an hour or so of the reminder — the app's most frequent
    // record by a distance, and the reason the sky has a rhythm at all.
    private static void AddDoses(List<CelestialEvent> events, Random rng, DateTime day)
    {
        foreach (var hour in new[] { 8, 20 })
        {
            if (rng.NextDouble() < 0.08)
                continue;

            var skipped = rng.NextDouble() < 0.04;
            events.Add(new CelestialEvent(
                day.AddHours(hour).AddMinutes(rng.Next(-25, 46)),
                CelestialCategory.Medication,
                "Phenobarbital",
                Loc.GetString(skipped ? "Sky_DoseSkipped" : "Sky_DoseGiven")));
        }
    }

    private static void AddMood(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.7)
            return;

        var mood = (MoodLevel)rng.Next(2, 6);
        events.Add(new CelestialEvent(
            day.AddHours(rng.Next(17, 23)).AddMinutes(rng.Next(60)),
            CelestialCategory.Mood,
            Loc.GetString("Journal_MoodTitle"),
            mood.GetDisplayName()));
    }

    private static void AddWeight(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (day.DayOfWeek != DayOfWeek.Sunday || rng.NextDouble() > 0.8)
            return;

        var kg = 24.4m + (decimal)(rng.NextDouble() * 0.8 - 0.4);
        events.Add(new CelestialEvent(
            day.AddHours(10).AddMinutes(rng.Next(60)),
            CelestialCategory.Weight,
            Loc.GetString("Journal_WeighIn"),
            kg.ToString("0.0", CultureInfo.CurrentCulture) + Loc.GetString("Common_KgSuffix")));
    }

    /// <summary>A curve day is three readings in six hours — a tight cluster, which is
    /// what the fan exists to spread without stacking.</summary>
    private static void AddGlucose(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.55)
            return;

        var start = day.AddHours(7).AddMinutes(rng.Next(90));
        var readings = rng.NextDouble() < 0.3 ? 5 : 3;

        for (int i = 0; i < readings; i++)
        {
            var value = 6m + (decimal)(rng.NextDouble() * 12);
            events.Add(new CelestialEvent(
                start.AddHours(i * 2).AddMinutes(rng.Next(-15, 16)),
                CelestialCategory.Glucose,
                Loc.GetString("Journal_GlucoseCheck"),
                Loc.Format("Journal_GlucoseTimeline", value.ToString("0.0", CultureInfo.CurrentCulture))));
        }
    }

    // Both shapes appear, as they do in real use: an exact weight of food some days,
    // a word the rest. They are never merged into one reading.
    private static void AddAppetite(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.65)
            return;

        var measured = rng.NextDouble() < 0.45;
        var detail = measured
            ? Loc.Format("Journal_AppetiteGrams", rng.Next(180, 420).ToString(CultureInfo.CurrentCulture))
            : Loc.Format("Journal_AteWord", ((AppetiteLevel)rng.Next(2, 6)).GetDisplayName().ToLowerInvariant());

        events.Add(new CelestialEvent(
            day.AddHours(rng.Next(7, 20)).AddMinutes(rng.Next(60)),
            CelestialCategory.Appetite,
            Loc.GetString("Journal_Appetite"),
            detail));
    }

    private static void AddWater(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.5)
            return;

        var count = rng.Next(1, 4);
        for (int i = 0; i < count; i++)
        {
            events.Add(new CelestialEvent(
                day.AddHours(rng.Next(7, 22)).AddMinutes(rng.Next(60)),
                CelestialCategory.Water,
                Loc.GetString("Journal_Water"),
                Loc.Format("Journal_WaterMl", rng.Next(120, 460).ToString(CultureInfo.CurrentCulture))));
        }
    }

    /// <summary>Rare, and sometimes clustered — a cluster seizure is two or three
    /// inside an hour. Deliberately the sparsest thing in the sky: it has to stay a
    /// few bright points in a year, never a texture.</summary>
    private static void AddSeizures(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.055)
            return;

        var first = day.AddHours(rng.Next(0, 24)).AddMinutes(rng.Next(60));
        var count = rng.NextDouble() < 0.35 ? rng.Next(2, 4) : 1;

        for (int i = 0; i < count; i++)
        {
            var minutes = rng.Next(1, 5);
            events.Add(new CelestialEvent(
                first.AddMinutes(i * rng.Next(12, 40)),
                CelestialCategory.Seizure,
                Loc.GetString("Journal_Seizure"),
                Loc.Format("Journal_SeizureDuration", minutes)));
        }
    }

    private static void AddCustom(List<CelestialEvent> events, Random rng, DateTime day)
    {
        if (rng.NextDouble() > 0.6)
            return;

        var count = rng.Next(1, 3);
        for (int i = 0; i < count; i++)
        {
            events.Add(new CelestialEvent(
                day.AddHours(rng.Next(7, 21)).AddMinutes(rng.Next(60)),
                CelestialCategory.Custom,
                Loc.GetString("CustomPreset_Walk"),
                $"{rng.Next(15, 65)} {Loc.GetString("CustomPreset_WalkUnit")}"));
        }
    }
}
