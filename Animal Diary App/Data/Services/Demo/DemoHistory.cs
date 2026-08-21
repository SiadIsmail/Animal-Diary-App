namespace Animal_Diary_App.Data.Services.Demo;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Building a demo pet's year of history.
//
//  This produces ENTITY ROWS, not a read model. That is the whole design: once the
//  rows are in SQLite, the Constellation, the vet report, Today's cards, the Journal
//  chips and the timeline are all just the app reading its own database. There is no
//  projection layer, no per-surface plumbing, and no second code path that can drift
//  from the real one.
//
//  Pure and MAUI-free, so the planted structures can be proven in the test project,
//  which matters more here than it looks: every one of them is invisible until someone
//  opens a lens and finds nothing there, on camera.
//
//  Nothing here writes. The seeder owns the transaction and the foreign keys; this
//  file owns the shape of the history and hands back rows with their parents grouped.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A parent row with the children that point at it. Grouped rather than
/// flattened because local ids do not exist until the parent is inserted: the seeder
/// inserts, reads the id back, and stamps the children with it.</summary>
public sealed class DemoMedicationRows
{
    public required Medication Medication { get; init; }
    public required List<MedicationSchedule> Schedules { get; init; }
    public required List<MedicationDoseLog> DoseLogs { get; init; }
}

/// <summary>Same, for an owner-defined tracker and its entries.</summary>
public sealed class DemoCustomRows
{
    public required CustomTracker Tracker { get; init; }
    public required List<CustomEntry> Entries { get; init; }
}

/// <summary>
/// One demo pet's complete seeded history, ready to insert.
///
/// <para>There is deliberately no <c>Pet</c> row here. The pet's identity is already fully
/// stated by the <see cref="DemoProfile"/>, and <c>Pet</c> resolves a photo path through
/// <c>PetPhotoService</c>: building one would drag MAUI into a file whose whole value is
/// being testable. The seeder builds the pet (and sets <c>IsDemo</c>); this builds the
/// history, which is what the name says.</para>
/// </summary>
public sealed class DemoSeed
{
    public List<PetCondition> Conditions { get; } = new();
    public List<Tracker> Trackers { get; } = new();
    public List<DemoMedicationRows> Medications { get; } = new();
    public List<DemoCustomRows> CustomTrackers { get; } = new();
    public List<PetEntry> Entries { get; } = new();
    public List<GlucoseEntry> Glucose { get; } = new();
    public List<AppetiteEntry> AppetiteLevels { get; } = new();
    public List<AppetiteAmountEntry> AppetiteAmounts { get; } = new();
    public List<SeizureEntry> Seizures { get; } = new();
    public List<WaterAmountEntry> WaterAmounts { get; } = new();
    public List<WaterLevelEntry> WaterLevels { get; } = new();

    /// <summary>Every row this seed will insert: the seeder's sanity check, and what the
    /// test asserting a year lands in the right order of magnitude counts.</summary>
    public int RowCount =>
        Conditions.Count + Trackers.Count
        + Medications.Sum(m => 1 + m.Schedules.Count + m.DoseLogs.Count)
        + CustomTrackers.Sum(c => 1 + c.Entries.Count)
        + Entries.Count + Glucose.Count + AppetiteLevels.Count + AppetiteAmounts.Count
        + Seizures.Count + WaterAmounts.Count + WaterLevels.Count;
}

public static class DemoHistory
{
    /// <summary>
    /// Generate one demo pet's history, ending today.
    /// </summary>
    /// <param name="profile">Who the pet is and what shape their history takes.</param>
    /// <param name="today">The last day of the history.</param>
    /// <param name="localize">Resolves an AppStrings key: used only for the custom
    /// trackers' preset names. Passed in rather than reached for, so this stays pure;
    /// tests hand it the identity function.</param>
    public static DemoSeed Build(DemoProfile profile, DateTime today, Func<string, string> localize)
    {
        // Seeded from the profile, NOT the clock: a creator who re-shoots a take tomorrow
        // must get the identical sky, or the second take does not match the first.
        var rng = new Random(StableSeed(profile.Id));

        var to = today.Date;
        var from = to.AddDays(-(profile.HistoryDays - 1));

        var seed = new DemoSeed();

        foreach (var conditionId in profile.ConditionIds)
            seed.Conditions.Add(new PetCondition { ConditionId = conditionId });

        foreach (var t in profile.Trackers)
            seed.Trackers.Add(new Tracker
            {
                TrackerId = t.Id,
                Kind = t.Kind,
                PerDayCount = t.PerDayCount,
                Unit = t.Unit,
                FromCondition = t.FromCondition,
            });

        BuildMedications(seed, profile, rng, from, to);
        BuildCustomTrackers(seed, profile, rng, from, to, localize);
        BuildDailyEntries(seed, profile, rng, from, to);
        BuildSeizures(seed, profile, rng, from, to);

        return seed;
    }

    // ── Medications and their adherence ──────────────────────────────────────────

    private static void BuildMedications(
        DemoSeed seed, DemoProfile profile, Random rng, DateTime from, DateTime to)
    {
        foreach (var med in profile.Medications)
        {
            var rows = new DemoMedicationRows
            {
                Medication = new Medication
                {
                    Name = med.Name,
                    Dosage = med.Dose,
                    Unit = med.Unit,
                    Notes = med.Notes,
                    // Bounds the app's own missed-dose reconciliation. Set to the start of
                    // the history so the sweep never invents Missed rows for days before
                    // this pet supposedly existed.
                    CreatedAt = from,
                },
                Schedules = new List<MedicationSchedule>(),
                DoseLogs = new List<MedicationDoseLog>(),
            };

            // One schedule row per (day, time): the shape the reminder engine expands.
            foreach (var day in med.Days)
                foreach (var time in med.Times)
                    rows.Schedules.Add(new MedicationSchedule { Day = day, Time = time });

            var lastTime = med.Times.Max();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (!med.Days.Contains(day.DayOfWeek) || IsQuiet(profile, to, day))
                    continue;

                foreach (var time in med.Times)
                {
                    // No row at all: nobody opened the app. A scheduled dose with no
                    // outcome is "not yet acted on", which is a true state, not a gap.
                    if (rng.NextDouble() < med.UnloggedRate * Sparseness(profile, from, to, day))
                        continue;

                    var skipped = rng.NextDouble() < med.SkipRate;

                    // The weekday-ring story: the LAST dose of the day runs late more often
                    // on Fri/Sat, because that is when a household's evening moves. Folded
                    // at seven days this becomes a visible spoke: the one pattern an owner
                    // can actually do something about.
                    var weekend = day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
                    var lateOdds = 0.10 * (weekend && time == lastTime ? med.WeekendLateMultiplier : 1.0);
                    var late = rng.NextDouble() < lateOdds;

                    var offset = late
                        ? rng.Next(75, 190)                 // properly late, past the hour
                        : rng.Next(-25, 46);                // around the reminder

                    rows.DoseLogs.Add(new MedicationDoseLog
                    {
                        ScheduledDate = day,
                        ScheduledTime = time,
                        Status = skipped ? DoseStatus.Skipped : DoseStatus.Taken,
                        ResolvedAt = day.Add(time).AddMinutes(offset),
                    });
                }
            }

            seed.Medications.Add(rows);
        }
    }

    // ── Owner-defined trackers ───────────────────────────────────────────────────

    private static void BuildCustomTrackers(
        DemoSeed seed, DemoProfile profile, Random rng, DateTime from, DateTime to,
        Func<string, string> localize)
    {
        foreach (var habit in profile.CustomTrackers)
        {
            var preset = habit.Preset;
            var rows = new DemoCustomRows
            {
                // Built exactly as the "add your own" sheet builds one, straight from the
                // preset, so a demo pet's custom tracker is indistinguishable from an
                // owner's, and nothing downstream needs to know it was seeded.
                Tracker = new CustomTracker
                {
                    Name = localize(preset.NameKey),
                    Icon = preset.Icon,
                    ColorKey = preset.ColorKey,
                    Shape = preset.Shape,
                    Unit = preset.UnitKey is null ? string.Empty : localize(preset.UnitKey),
                    Kind = preset.Kind,
                    PerDayCount = preset.PerDayCount,
                    IncludeInReport = preset.InReport,
                },
                Entries = new List<CustomEntry>(),
            };

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (IsSilent(profile, rng, from, to, day))
                    continue;

                if (rng.NextDouble() > habit.DayRate * Sparseness(profile, from, to, day))
                    continue;

                var count = RandInt(rng, habit.PerDay);
                var weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                for (var i = 0; i < count; i++)
                {
                    decimal? amount = null;
                    if (preset.Shape == CustomShape.Amount)
                    {
                        var value = Rand(rng, habit.Amount);
                        if (weekend)
                            value *= 1 + habit.WeekendAmountBonus;
                        amount = Math.Round((decimal)value);
                    }

                    rows.Entries.Add(new CustomEntry
                    {
                        Date = day,
                        Time = RandTime(rng, habit.Window),
                        Amount = amount,
                    });
                }
            }

            seed.CustomTrackers.Add(rows);
        }
    }

    // ── Mood, weight, glucose, appetite, water ───────────────────────────────────

    private static void BuildDailyEntries(
        DemoSeed seed, DemoProfile profile, Random rng, DateTime from, DateTime to)
    {
        var totalDays = Math.Max(1, (to - from).TotalDays);

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (IsSilent(profile, rng, from, to, day))
                continue;

            var density = Sparseness(profile, from, to, day);

            // Mood and weight share one row per day (PetEntry), so they are collected
            // together and written once: two rows for one day would read as two days.
            var loggedMood = rng.NextDouble() < profile.Mood.DayRate * density;
            var loggedWeight = day.DayOfWeek == profile.Weight.OnDay && rng.NextDouble() < 0.8;

            if (loggedMood || loggedWeight)
            {
                var entry = new PetEntry { Date = day };

                if (loggedMood)
                {
                    var time = RandTime(rng, profile.Mood.Window);
                    entry.MoodLevel = RandInt(rng, profile.Mood.Level);
                    entry.MoodTimeTicks = time.Ticks;
                }

                if (loggedWeight)
                {
                    // A slow, signed drift across the year: the report's headline fact,
                    // stated neutrally. Never coloured good or bad anywhere.
                    var progress = (day - from).TotalDays / totalDays;
                    var target = profile.Weight.StartKg
                        + (profile.Weight.EndKg - profile.Weight.StartKg) * (decimal)progress;
                    var jitter = (decimal)((rng.NextDouble() - 0.5) * profile.Weight.Jitter);

                    entry.Weight = Math.Round(target + jitter, 2);
                    entry.WeightTimeTicks = new TimeSpan(10, rng.Next(60), 0).Ticks;
                }

                seed.Entries.Add(entry);
            }

            if (profile.Glucose is DemoGlucoseHabit glucose)
                BuildGlucoseDay(seed, glucose, rng, day, density);

            if (profile.Appetite is DemoAppetiteHabit appetite)
                BuildAppetiteDay(seed, appetite, rng, day, density);

            if (profile.Water is DemoWaterHabit water)
                BuildWaterDay(seed, water, rng, day, density);
        }
    }

    /// <summary>
    /// A curve day: several readings spread across one window.
    ///
    /// <para>The readings sit in two tight windows around the insulin times, which is what
    /// a twice-daily routine actually produces, and folded at a day it draws two arcs
    /// bracketing the dose spokes. That is the Cycle lens proving itself on a pet with no
    /// seizures at all.</para>
    /// </summary>
    private static void BuildGlucoseDay(
        DemoSeed seed, DemoGlucoseHabit habit, Random rng, DateTime day, double density)
    {
        if (rng.NextDouble() > habit.DayRate * density)
            return;

        var readings = RandInt(rng, habit.Readings);
        var evening = readings / 2;

        for (var i = 0; i < readings; i++)
        {
            var window = i < readings - evening ? habit.MorningWindow : habit.EveningWindow;
            seed.Glucose.Add(new GlucoseEntry
            {
                Date = day,
                Time = RandTime(rng, window),
                Value = Math.Round((decimal)Rand(rng, habit.Value), 1),
                Context = i == 0 ? FoodContext.BeforeFood : FoodContext.AfterFood,
            });
        }
    }

    /// <summary>Both shapes appear, as they do in real use: an exact weight of food some
    /// days, a word the rest. They are never merged into one reading: the report keeps
    /// two separate graphs and this is what fills both.</summary>
    private static void BuildAppetiteDay(
        DemoSeed seed, DemoAppetiteHabit habit, Random rng, DateTime day, double density)
    {
        if (rng.NextDouble() > habit.DayRate * density)
            return;

        var food = habit.Foods[rng.Next(habit.Foods.Length)];

        if (rng.NextDouble() < habit.MeasuredShare)
            seed.AppetiteAmounts.Add(new AppetiteAmountEntry
            {
                Date = day,
                Time = RandTime(rng, new HourBand(7, 19)),
                Grams = Math.Round((decimal)Rand(rng, habit.Grams)),
                Food = food,
            });
        else
            seed.AppetiteLevels.Add(new AppetiteEntry
            {
                Date = day,
                Time = RandTime(rng, new HourBand(7, 19)),
                Level = RandInt(rng, habit.Level),
                Food = food,
            });
    }

    private static void BuildWaterDay(
        DemoSeed seed, DemoWaterHabit habit, Random rng, DateTime day, double density)
    {
        if (rng.NextDouble() > habit.DayRate * density)
            return;

        if (rng.NextDouble() < habit.MeasuredShare)
        {
            // Additive: the owner logs each drink, and the report sums the day.
            var count = RandInt(rng, habit.PerDay);
            for (var i = 0; i < count; i++)
                seed.WaterAmounts.Add(new WaterAmountEntry
                {
                    Date = day,
                    Time = RandTime(rng, new HourBand(7, 22)),
                    AmountMl = Math.Round((decimal)Rand(rng, habit.Ml)),
                });
        }
        else
        {
            seed.WaterLevels.Add(new WaterLevelEntry
            {
                Date = day,
                Time = RandTime(rng, new HourBand(18, 22)),
                Level = RandInt(rng, habit.Level),
            });
        }
    }

    // ── Seizures: the structure the Constellation exists to reveal ───────────────

    /// <summary>
    /// Two independent structures in one small set of events.
    ///
    /// <para><b>Clusters</b> land roughly <c>ClusterSpacingDays</c> apart with jitter, so a
    /// fold near that period gathers them at one angle: the "does it come round again"
    /// question. <b>Within</b> a cluster most seizures fall in the small hours, so a fold at
    /// a day collapses the whole year into a wedge, and Nights draws a vertical band. The
    /// two are orthogonal: turning the dial from one day to a fortnight rearranges the same
    /// stars into a different answer, which is the single most persuasive thing this surface
    /// can show.</para>
    ///
    /// <para><b>There is no total.</b> Clusters run at their spacing for the whole range and
    /// the year's count falls out of that. An earlier version spent a fixed budget of
    /// fourteen front-to-back, which quietly emptied the most recent 180 days, so the pet
    /// built to demonstrate seizures had none anywhere the 7-, 30- or 90-day ranges were
    /// looking, and they appeared only on the 365-day view where 1,700 other stars are
    /// already competing for attention.</para>
    /// </summary>
    private static void BuildSeizures(
        DemoSeed seed, DemoProfile profile, Random rng, DateTime from, DateTime to)
    {
        if (profile.Seizures is not DemoSeizurePattern pattern)
            return;

        // Start part-way into the first interval so the run does not always open on day one.
        var day = from.AddDays(rng.Next(pattern.ClusterSpacingDays));

        while (day <= to)
        {
            if (!IsQuiet(profile, to, day))
            {
                // Most nights are a single event; a cluster is the notable minority.
                var size = rng.NextDouble() < pattern.MultiShare
                    ? rng.Next(2, pattern.MaxPerCluster + 1)
                    : 1;

                // A cluster is two or three inside about an hour: the threshold a lot of
                // dogs' emergency plans hang on, and one crowded row in Nights.
                //
                // The offsets are worked out BEFORE the start time so the whole cluster can
                // be placed inside the night band rather than just its first event. Drawing
                // the start first and letting members drift forward is what the obvious
                // version does, and it silently walks the tail of every cluster out into the
                // morning: the band ends up describing where a cluster OPENS, which is not
                // what the sky draws or what anyone reading the profile would expect.
                var offsets = new int[size];
                for (var i = 1; i < size; i++)
                    offsets[i] = offsets[i - 1] + rng.Next(12, 45);

                var first = day.Add(ClusterStart(rng, pattern, offsets[^1]));

                for (var i = 0; i < size; i++)
                {
                    var at = first.AddMinutes(offsets[i]);
                    seed.Seizures.Add(new SeizureEntry
                    {
                        Date = at.Date,
                        Time = at.TimeOfDay,
                        DurationMinutes = RandInt(rng, pattern.DurationMinutes),
                        // Left null often: not knowing is a normal answer, and the details
                        // column has to read properly both ways.
                        Type = rng.NextDouble() < 0.6
                            ? (SeizureType)rng.Next(1, 4)
                            : null,
                    });
                }
            }

            var jitter = rng.Next(-pattern.ClusterJitterDays, pattern.ClusterJitterDays + 1);
            day = day.AddDays(Math.Max(1, pattern.ClusterSpacingDays + jitter));
        }
    }

    /// <summary>
    /// When a cluster starts: usually in the small hours, sometimes anywhere.
    ///
    /// <para>The scattered minority is not noise for its own sake: a perfect band reads as
    /// synthetic, and the honest picture is a tendency rather than a rule.</para>
    /// </summary>
    /// <param name="span">Minutes from the cluster's first event to its last. The start is
    /// pulled back by this much so the whole cluster fits inside the band; when a cluster is
    /// longer than the band itself the start simply pins to the band's beginning.</param>
    private static TimeSpan ClusterStart(Random rng, DemoSeizurePattern pattern, int span)
    {
        if (rng.NextDouble() >= pattern.NightShare)
            return new TimeSpan(rng.Next(24), rng.Next(60), 0);

        var band = pattern.NightBand;
        var firstMinute = band.From * 60;
        var lastMinute = band.To * 60 + 59;
        var latestStart = Math.Max(firstMinute, lastMinute - span);

        return TimeSpan.FromMinutes(rng.Next(firstMinute, latestStart + 1));
    }

    // ── The shared texture ───────────────────────────────────────────────────────

    /// <summary>Inside the quiet spell: a stretch where nothing at all was written down.
    /// Checked separately from <see cref="IsSilent"/> because it must be deterministic:
    /// the same fortnight has to be empty for every signal, or it reads as a glitch rather
    /// than a fortnight the owner had other things on.</summary>
    private static bool IsQuiet(DemoProfile profile, DateTime to, DateTime day)
    {
        var start = to.AddDays(-profile.QuietSpellStartDaysAgo);
        return day >= start && day < start.AddDays(profile.QuietSpellLength);
    }

    /// <summary>This day gets nothing: either the quiet spell, or one of the ordinary days
    /// nobody manages to write anything down. Both are normal, and the sky must read as
    /// calm rather than broken.</summary>
    private static bool IsSilent(DemoProfile profile, Random rng, DateTime from, DateTime to, DateTime day) =>
        IsQuiet(profile, to, day) || rng.NextDouble() < profile.SilentDayRate;

    /// <summary>
    /// How much of the day's usual logging actually happened, 0..1: thin at the start of
    /// the history, full by the time the ramp finishes.
    ///
    /// <para>A new owner records little and builds the habit. In the Nights lens that reads
    /// as the wall visibly thickening as the eye travels down, which is one of the four
    /// questions that lens answers and the only one that needs a year to show.</para>
    /// </summary>
    private static double Sparseness(DemoProfile profile, DateTime from, DateTime to, DateTime day)
    {
        var total = Math.Max(1, (to - from).TotalDays);
        var progress = (day - from).TotalDays / total;
        var ramp = Math.Max(0.0001, profile.DensityRampFraction);

        if (progress >= ramp)
            return 1;

        return profile.EarlyDensityFloor
            + (1 - profile.EarlyDensityFloor) * (progress / ramp);
    }

    // ── Small helpers ────────────────────────────────────────────────────────────

    private static double Rand(Random rng, Band band) =>
        band.Lo + rng.NextDouble() * (band.Hi - band.Lo);

    private static int RandInt(Random rng, Band band) =>
        rng.Next((int)band.Lo, (int)band.Hi + 1);

    /// <summary>A time inside an hour band. <c>From &gt; To</c> wraps midnight, so a band
    /// like 22–3 works without the caller special-casing it.</summary>
    private static TimeSpan RandTime(Random rng, HourBand band)
    {
        var span = band.To >= band.From ? band.To - band.From : 24 - band.From + band.To;
        var hour = (band.From + rng.Next(span + 1)) % 24;
        return new TimeSpan(hour, rng.Next(60), 0);
    }

    /// <summary>
    /// A stable seed from the profile id.
    ///
    /// <para>FNV-1a and not <c>string.GetHashCode</c>, for the same reason
    /// <c>ConstellationAsterism</c> avoids it: the framework's string hash is randomized
    /// per process, so the "deterministic" history would differ on every launch and a
    /// creator's second take would not match their first.</para>
    /// </summary>
    private static int StableSeed(string id)
    {
        unchecked
        {
            const uint offset = 2166136261;
            const uint prime = 16777619;

            var hash = offset;
            foreach (var c in id)
            {
                hash ^= c;
                hash *= prime;
            }

            return (int)hash;
        }
    }
}
