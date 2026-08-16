namespace Animal_Diary_App.Data.Services.Demo;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The demo pets, as data.
//
//  A profile is the whole description of one seeded animal: who they are, what they
//  are treated for, and — the part that matters — the SHAPE of the history that gets
//  generated for them. Every knob the generator has is a field here, so the two pets
//  can be read side by side and changed without opening DemoHistory.
//
//  On the structure being planted: yes, these histories contain patterns, and that is
//  the point of a demo. The line the app must not cross is drawn one layer up, in the
//  UI: Felova still never picks the fold period, never labels an alignment and never
//  colours anything by frequency. The fiction may hold a pattern; the app must not
//  point at it. The creator narrates. (AI/design-decisions.md, "The Constellation
//  encodes time, and nothing else".)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>An inclusive band of whole hours on a 24-hour clock, e.g. 2–5 for the small
/// hours. <see cref="From"/> may exceed <see cref="To"/> to wrap midnight.</summary>
public readonly record struct HourBand(int From, int To);

/// <summary>An inclusive numeric band, used for readings and amounts.</summary>
public readonly record struct Band(double Lo, double Hi);

/// <summary>One medication on the demo pet, plus how reliably it actually gets given.</summary>
/// <param name="Times">Reminder times; one <c>MedicationSchedule</c> row per (day, time).</param>
/// <param name="Days">Which weekdays it is due.</param>
/// <param name="UnloggedRate">Doses with no row at all — nobody touched the app.</param>
/// <param name="SkipRate">Doses explicitly marked Skipped.</param>
/// <param name="WeekendLateMultiplier">How much more often the LAST dose of the day runs
/// late on Fri/Sat. This is the weekday-ring story: a fold at seven days turns a household
/// routine into a visible spoke, which is the one pattern an owner can actually act on.</param>
public sealed record DemoMedication(
    string Name,
    decimal Dose,
    string Unit,
    TimeSpan[] Times,
    DayOfWeek[] Days,
    double UnloggedRate,
    double SkipRate,
    double WeekendLateMultiplier,
    string Notes = "");

/// <summary>A built-in tracker on the pet's care plan.</summary>
public sealed record DemoTracker(
    TrackerId Id,
    TrackerKind Kind,
    int PerDayCount = 0,
    string Unit = "",
    string? FromCondition = null);

/// <summary>
/// The seizure history's shape — the single most important thing in this file, because
/// it is what makes the Constellation's lenses show anything at all.
///
/// <para>The existing dev fixture placed seizures at a uniformly random hour, which folds
/// to noise at every period and left the Cycle lens with nothing to demonstrate. Two
/// INDEPENDENT structures are planted here instead, and the pay-off is being able to turn
/// one dial and watch the same stars rearrange into a different answer.</para>
/// </summary>
/// <param name="NightBand">Where most of them land — the wedge on a one-day fold, and the
/// vertical band under the daylight wash in Nights.</param>
/// <param name="NightShare">The rest are scattered across the clock, because a real diary
/// is not tidy and a perfect band reads as fake.</param>
/// <param name="ClusterSpacingDays">Roughly how far apart the clusters sit — the answer to
/// "does it come round again", visible only when folded near this period. <b>This alone
/// decides how many there are in a year</b>; there is deliberately no total, because a
/// budget spent front-to-back empties the recent end of the history, which is the end
/// every short range is looking at.</param>
/// <param name="ClusterJitterDays">Load-bearing. An exact period looks synthetic and, worse,
/// implies the app can detect periodicity — which is precisely the claim it must not make.</param>
/// <param name="MultiShare">How often a night carries more than one. Most seizures are
/// single events; a cluster of two or three inside an hour is the notable minority — and
/// the threshold a lot of dogs' emergency plans hang on, so it has to be in there.</param>
public sealed record DemoSeizurePattern(
    HourBand NightBand,
    double NightShare,
    int ClusterSpacingDays,
    int ClusterJitterDays,
    double MultiShare,
    int MaxPerCluster,
    Band DurationMinutes);

/// <summary>Glucose curve days: several readings in one stretch, at the hours a twice-daily
/// insulin routine produces. Two windows, so a one-day fold shows two arcs bracketing the
/// dose spokes — proof the Cycle lens is not an epilepsy-only trick.</summary>
public sealed record DemoGlucoseHabit(
    double DayRate,
    HourBand MorningWindow,
    HourBand EveningWindow,
    Band Readings,
    Band Value);

/// <summary>A slow, signed weight change — the vet report's headline fact, stated
/// neutrally and never coloured good or bad.</summary>
public sealed record DemoWeightTrend(decimal StartKg, decimal EndKg, DayOfWeek OnDay, double Jitter);

public sealed record DemoMoodHabit(double DayRate, HourBand Window, Band Level);

/// <summary>Both appetite modes, as real use produces them: an exact weight of food some
/// days, a word the rest. They are never merged — the report keeps two graphs.</summary>
public sealed record DemoAppetiteHabit(double DayRate, double MeasuredShare, Band Grams, Band Level, string[] Foods);

/// <summary>Both water modes, same reason as appetite.</summary>
public sealed record DemoWaterHabit(double DayRate, double MeasuredShare, Band Ml, Band Level, Band PerDay);

/// <summary>One owner-defined tracker and how often it gets logged. The preset carries the
/// name key, icon, colour, shape and cadence — a demo pet's custom trackers are made the
/// same way an owner makes theirs, so nothing about them is special-cased downstream.</summary>
public sealed record DemoCustomHabit(
    CustomTrackerPresets.Preset Preset,
    double DayRate,
    HourBand Window,
    Band Amount,
    Band PerDay,
    double WeekendAmountBonus = 0);

/// <summary>
/// Everything needed to seed one demo pet.
/// </summary>
public sealed record DemoProfile
{
    public required string Id { get; init; }

    /// <summary>The pet's name. <b>No "(Demo)" marker</b> — deliberately, so a creator's
    /// footage looks like the product rather than a test build. The row's
    /// <c>Pet.IsDemo</c> flag is what the app keys on, and it is what tells support
    /// whether a reported oddity came from seeded data.</summary>
    public required string Name { get; init; }

    /// <summary>A <c>PetTypeOption</c> key ("Dog", "Cat") — localized for display through
    /// the same helper the real pets use.</summary>
    public required string Species { get; init; }

    public required int AgeYears { get; init; }

    /// <summary>Condition ids from <see cref="ConditionCatalog"/>.</summary>
    public required string[] ConditionIds { get; init; }

    public required DemoMedication[] Medications { get; init; }
    public required DemoTracker[] Trackers { get; init; }
    public required DemoCustomHabit[] CustomTrackers { get; init; }

    public required DemoWeightTrend Weight { get; init; }
    public required DemoMoodHabit Mood { get; init; }
    public DemoSeizurePattern? Seizures { get; init; }
    public DemoGlucoseHabit? Glucose { get; init; }
    public DemoAppetiteHabit? Appetite { get; init; }
    public DemoWaterHabit? Water { get; init; }

    // ── Shared texture: the same for both pets, so a creator switching between them
    //    sees one product rather than two different apps ──────────────────────────

    /// <summary>How much history to generate. A full year, so the 365-day range is full
    /// and every shorter one is a subset of the same story.</summary>
    public int HistoryDays { get; init; } = 365;

    /// <summary>Days where nothing at all was written down. Normal, and the sky must read
    /// as calm rather than broken.</summary>
    public double SilentDayRate { get; init; } = 0.06;

    /// <summary>A stretch with nothing in it — life happens, and Felova never scolds.</summary>
    public int QuietSpellStartDaysAgo { get; init; } = 96;
    public int QuietSpellLength { get; init; } = 14;

    /// <summary>How much thinner logging is at the START of the history than at the end.
    /// A new owner records little and builds the habit — in Nights that reads as the wall
    /// visibly thickening as the eye travels down, which is the lens's fourth question.</summary>
    public double EarlyDensityFloor { get; init; } = 0.35;

    /// <summary>How far through the history the ramp finishes.</summary>
    public double DensityRampFraction { get; init; } = 0.45;

    // ── The two pets ─────────────────────────────────────────────────────────────
    //
    //  Built per language rather than held as constants. Medication names and the food
    //  labels are stored as USER data — the app never re-translates them, exactly as it
    //  never re-translates a pet's name — so the language has to be chosen at the moment
    //  the history is seeded. A German creator filming an English drug list would
    //  misrepresent the product to their audience. Same reasoning, and the same shape, as
    //  VetReportSampleData.SampleNotes.
    //
    //  Taking a bool rather than reaching for LocalizationManager keeps this file free of
    //  MAUI and of hidden global state, so it can be linked into the test project.

    public static IReadOnlyList<DemoProfile> All(bool german) => new[] { Kira(german), Mira(german) };

    public static DemoProfile? Find(string id, bool german) =>
        All(german).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <b>Kira — epilepsy, dog.</b> The Constellation showcase.
    ///
    /// <para>Everything about this history exists to give the three lenses something true
    /// to reveal: a small-hours band (fold at a day), a Friday/Saturday dosing wobble
    /// (fold at a week), and clusters about four weeks apart (fold at 28). All three are
    /// in the same data at once, which is what makes turning the dial worth doing.</para>
    /// </summary>
    public static DemoProfile Kira(bool german) => new()
    {
        Id = "kira",
        Name = "Kira",
        Species = "Dog",
        AgeYears = 6,
        ConditionIds = new[] { "epilepsy" },

        Medications = new[]
        {
            // Two spokes on a one-day fold. The evening one is heavier because two drugs
            // land on it, which is also what makes "seizures against the evening dose"
            // a legible two-key focus comparison.
            new DemoMedication(
                "Phenobarbital", 60m, "mg",
                new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                AllWeek, UnloggedRate: 0.08, SkipRate: 0.04, WeekendLateMultiplier: 3.0),
            new DemoMedication(
                german ? "Kaliumbromid" : "Potassium bromide", 500m, "mg",
                new[] { new TimeSpan(20, 0, 0) },
                AllWeek, UnloggedRate: 0.09, SkipRate: 0.05, WeekendLateMultiplier: 3.0),
        },

        Trackers = new[]
        {
            new DemoTracker(TrackerId.Seizure, TrackerKind.Event, FromCondition: "epilepsy"),
            new DemoTracker(TrackerId.Mood, TrackerKind.Daily),
            new DemoTracker(TrackerId.Weight, TrackerKind.Weekly),
            new DemoTracker(TrackerId.Appetite, TrackerKind.Daily),
        },

        // The Amount shape. Walks are the archetypal non-clinical tracker and the preset
        // ships with InReport off, which is correct and worth showing.
        CustomTrackers = new[]
        {
            new DemoCustomHabit(
                CustomTrackerPresets.All[0],           // Walk — Amount, "min"
                DayRate: 0.72,
                Window: new HourBand(7, 20),
                Amount: new Band(15, 65),
                PerDay: new Band(1, 2),
                WeekendAmountBonus: 0.35),
        },

        Weight = new DemoWeightTrend(24.6m, 24.2m, DayOfWeek.Sunday, Jitter: 0.4),
        Mood = new DemoMoodHabit(DayRate: 0.70, Window: new HourBand(17, 22), Level: new Band(2, 5)),

        Seizures = new DemoSeizurePattern(
            NightBand: new HourBand(2, 5),
            // Drawn per NIGHT, not per seizure — nominally 3 nights in 4 in the band and
            // the rest anywhere. That asymmetry is wanted: the wedge has to be obvious at
            // arm's length on a phone, and it also has to have something outside it,
            // because a band with no exceptions reads as synthetic and quietly claims more
            // than a diary can.
            //
            // Kira's seed happens to realize this HIGH — 23 of her 25 nights land in the
            // band rather than the ~19 the number implies. That is one seed's luck (p≈0.04)
            // and it is left alone deliberately: tuning the constant until one fixed seed
            // produced the textbook split would make it lie about intent to everyone who
            // reads it afterwards. The tests assert the properties — a strong majority, and
            // at least one exception — not a count.
            NightShare: 0.75,
            // A FORTNIGHT, not a month. Two reasons, and the first is the one that matters:
            // at monthly spacing a 30-day range — the one the page opens on — held about
            // one seizure, so the story this pet exists to tell was invisible until someone
            // widened to a year, by which point the sky has ~1,700 stars in it. At a
            // fortnight, 30 days carries two or three nights and 90 days carries six.
            //
            // The second is a windfall: the fold dial only reaches half the range on screen
            // (MaxPeriodDays), so a 27-day fold needed the 90-day chip. A 14-day one is
            // available at 30 days, which puts "does it come round again" on the DEFAULT
            // range instead of two taps away.
            ClusterSpacingDays: 14,
            ClusterJitterDays: 3,
            MultiShare: 0.4,
            MaxPerCluster: 3,
            DurationMinutes: new Band(1, 4)),

        Appetite = new DemoAppetiteHabit(
            DayRate: 0.65, MeasuredShare: 0.45,
            Grams: new Band(180, 420), Level: new Band(2, 5),
            Foods: german
                ? new[] { "Trockenfutter", "Nassfutter (Huhn)" }
                : new[] { "Dry food", "Wet food (chicken)" }),
    };

    /// <summary>
    /// <b>Mira — diabetes, cat.</b> The vet-report showcase.
    ///
    /// <para>Deliberately has <b>no seizures at all</b>: the legend lists only what is
    /// present, and a cat that has never seized must never be given a seizure row to make
    /// the demo look fuller. Her glucose curve days carry the Cycle story instead — two
    /// arcs bracketing the insulin spokes — so the lens proves itself on a pet with a
    /// completely different condition.</para>
    /// </summary>
    public static DemoProfile Mira(bool german) => new()
    {
        Id = "mira",
        Name = "Mira",
        Species = "Cat",
        AgeYears = 9,
        ConditionIds = new[] { "diabetes" },

        Medications = new[]
        {
            new DemoMedication(
                "ProZinc (Insulin)", 2m, "IU",
                new[] { new TimeSpan(8, 0, 0), new TimeSpan(20, 0, 0) },
                AllWeek, UnloggedRate: 0.05, SkipRate: 0.02, WeekendLateMultiplier: 2.0),
            new DemoMedication(
                german ? "Mirtazapin" : "Mirtazapine", 1.88m, "mg",
                new[] { new TimeSpan(20, 0, 0) },
                new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
                UnloggedRate: 0.10, SkipRate: 0.08, WeekendLateMultiplier: 1.0),
        },

        Trackers = new[]
        {
            new DemoTracker(TrackerId.Glucose, TrackerKind.PerDay, PerDayCount: 2, Unit: "mmol/L", FromCondition: "diabetes"),
            new DemoTracker(TrackerId.Water, TrackerKind.Daily, FromCondition: "diabetes"),
            new DemoTracker(TrackerId.Appetite, TrackerKind.Daily, FromCondition: "diabetes"),
            new DemoTracker(TrackerId.Mood, TrackerKind.Daily),
            new DemoTracker(TrackerId.Weight, TrackerKind.Weekly),
        },

        // The Tick shape, and the one preset that ships with InReport ON — a sick episode
        // belongs in front of a vet where a walk does not.
        CustomTrackers = new[]
        {
            new DemoCustomHabit(
                CustomTrackerPresets.All[3],           // Sick — Tick, Event
                DayRate: 0.022,
                Window: new HourBand(6, 23),
                Amount: default,
                PerDay: new Band(1, 1)),
        },

        Weight = new DemoWeightTrend(5.9m, 5.2m, DayOfWeek.Sunday, Jitter: 0.06),
        Mood = new DemoMoodHabit(DayRate: 0.75, Window: new HourBand(17, 22), Level: new Band(2, 5)),

        Glucose = new DemoGlucoseHabit(
            DayRate: 0.55,
            MorningWindow: new HourBand(7, 9),
            EveningWindow: new HourBand(19, 21),
            Readings: new Band(3, 5),
            Value: new Band(6, 18)),

        Appetite = new DemoAppetiteHabit(
            DayRate: 0.70, MeasuredShare: 0.45,
            Grams: new Band(120, 230), Level: new Band(2, 5),
            Foods: german
                ? new[] { "Nassfutter (Huhn)", "Diätfutter kohlenhydratarm" }
                : new[] { "Wet food (chicken)", "Low-carb diabetic wet food" }),

        Water = new DemoWaterHabit(
            DayRate: 0.60, MeasuredShare: 0.5,
            Ml: new Band(120, 460), Level: new Band(2, 5),
            PerDay: new Band(1, 3)),
    };

    private static readonly DayOfWeek[] AllWeek =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };
}
