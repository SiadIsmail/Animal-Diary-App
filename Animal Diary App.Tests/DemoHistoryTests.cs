namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Demo;
using Xunit;

/// <summary>
/// The structures the demo histories plant.
///
/// <para><b>Why these are tested at all:</b> every one of them is invisible until a creator
/// opens a lens, on camera, and finds nothing in it. The fixture this replaces placed
/// seizures at a uniformly random hour — which still produces a perfectly plausible-looking
/// sky, and folds to noise at every period. No build, no review and no other test catches
/// that. These assert the picture is actually there.</para>
///
/// <para>Thresholds are stated as properties ("most land in the small hours") rather than
/// pinned counts wherever sampling makes an exact number brittle — a test that breaks every
/// time someone nudges a probability teaches people to delete tests.</para>
/// </summary>
public class DemoHistoryTests
{
    private static readonly DateTime Today = new(2026, 8, 16);

    /// <summary>Keys pass straight through: the generator's only text dependency is the
    /// custom-tracker preset names, and nothing here asserts on words.</summary>
    private static DemoSeed Build(DemoProfile profile) =>
        DemoHistory.Build(profile, Today, k => k);

    private static DemoSeed Kira() => Build(DemoProfile.Kira(german: false));
    private static DemoSeed Mira() => Build(DemoProfile.Mira(german: false));

    // ── The Cycle lens, folded at a day ─────────────────────────────────────────

    /// <summary>
    /// The headline: most seizures land in the small hours, so a one-day fold collapses a
    /// year into a wedge instead of scattering it round the ring.
    ///
    /// <para>The bar is set against what UNIFORM would give — four hours out of twenty-four
    /// is ~17%. Anything near that is the old fixture's behaviour and a dead demo.</para>
    /// </summary>
    [Fact]
    public void KiraSeizures_ConcentrateInTheSmallHours()
    {
        var seizures = Kira().Seizures;
        var band = DemoProfile.Kira(german: false).Seizures!.NightBand;

        var inBand = seizures.Count(s => s.Time.Hours >= band.From && s.Time.Hours <= band.To);
        var share = (double)inBand / seizures.Count;

        Assert.True(share >= 0.5,
            $"only {inBand}/{seizures.Count} seizures fell in {band.From}:00–{band.To}:59 " +
            "— a one-day fold would show no wedge (uniform would be ~0.17)");
    }

    /// <summary>...but not ALL of them. A perfect band reads as synthetic, and the honest
    /// picture is a tendency rather than a rule.</summary>
    [Fact]
    public void KiraSeizures_AreNotPerfectlyBanded()
    {
        var seizures = Kira().Seizures;
        var band = DemoProfile.Kira(german: false).Seizures!.NightBand;

        Assert.Contains(seizures, s => s.Time.Hours < band.From || s.Time.Hours > band.To);
    }

    // ── The Cycle lens, folded at four weeks ────────────────────────────────────

    /// <summary>
    /// The second, INDEPENDENT structure: clusters roughly four weeks apart. Turning the
    /// dial from one day to twenty-eight has to rearrange the same stars into a different
    /// answer, or the fold dial is a control with one use.
    /// </summary>
    [Fact]
    public void KiraSeizures_ClusterAtRoughlyTheProfilesPeriod()
    {
        var pattern = DemoProfile.Kira(german: false).Seizures!;
        var days = Kira().Seizures.Select(s => s.Date).Distinct().OrderBy(d => d).ToList();

        // Same-day and next-day events are one cluster; a new cluster starts after a gap.
        var clusterStarts = new List<DateTime> { days[0] };
        for (var i = 1; i < days.Count; i++)
            if ((days[i] - days[i - 1]).TotalDays > 2)
                clusterStarts.Add(days[i]);

        Assert.True(clusterStarts.Count >= 4,
            $"only {clusterStarts.Count} clusters — too few for a period to be visible");

        var low = pattern.ClusterSpacingDays - pattern.ClusterJitterDays - 1;
        var high = pattern.ClusterSpacingDays + pattern.ClusterJitterDays + 1;

        foreach (var gap in clusterStarts.Zip(clusterStarts.Skip(1), (a, b) => (b - a).TotalDays))
        {
            // A cluster landing inside the quiet fortnight is skipped, which doubles that
            // one gap. Any other spacing means the period isn't there to be found.
            var single = gap >= low && gap <= high;
            var doubled = gap >= 2 * low && gap <= 2 * high;
            Assert.True(single || doubled, $"cluster gap of {gap} days fits no multiple of the period");
        }
    }

    /// <summary>Jitter is load-bearing: an exact period looks synthetic and implies the app
    /// can detect periodicity, which is precisely the claim it must never make.</summary>
    [Fact]
    public void KiraSeizures_ClusterSpacingVaries()
    {
        var days = Kira().Seizures.Select(s => s.Date).Distinct().OrderBy(d => d).ToList();

        var starts = new List<DateTime> { days[0] };
        for (var i = 1; i < days.Count; i++)
            if ((days[i] - days[i - 1]).TotalDays > 2)
                starts.Add(days[i]);

        var gaps = starts.Zip(starts.Skip(1), (a, b) => (int)(b - a).TotalDays).ToList();
        Assert.True(gaps.Distinct().Count() > 1, "every cluster gap is identical — that reads as a metronome");
    }

    /// <summary>
    /// <b>Seizures reach every range the picker offers.</b>
    ///
    /// <para>This is the test that should have existed from the start. An earlier version
    /// spent a fixed budget of fourteen seizures front-to-back across the year, which
    /// emptied the most recent 180 days entirely — so the pet built to demonstrate seizures
    /// had none at 7, 30 or 90 days, and they showed up only on the 365-day view where
    /// ~1,700 other stars are already competing. Everything else passed: the band was
    /// there, the clusters were there, the count was right. Nothing looked wrong until you
    /// opened the page.</para>
    ///
    /// <para>7 days is excluded on purpose — a fortnightly rhythm genuinely can miss a
    /// single week, and demanding otherwise would be asking the fixture to lie.</para>
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void KiraSeizures_AppearInEveryRangeTheConstellationOffers(int rangeDays)
    {
        var since = Today.AddDays(-(rangeDays - 1));
        var visible = Kira().Seizures.Count(s => s.Date >= since);

        Assert.True(visible >= 2,
            $"only {visible} seizure(s) in the last {rangeDays} days — the range opens on an empty story");
    }

    /// <summary>Still the rarest thing in the sky by a distance. Density is what makes them
    /// findable at a short range; it must not make them a texture at a long one.</summary>
    [Fact]
    public void KiraSeizures_StayTheRarestThingInTheSky()
    {
        var seed = Kira();
        var doses = seed.Medications.Sum(m => m.DoseLogs.Count);

        Assert.InRange(seed.Seizures.Count, 20, 60);
        Assert.True(seed.Seizures.Count * 8 < doses,
            "seizures are no longer an order of magnitude rarer than routine records");
    }

    /// <summary>The fold dial only reaches half the range on screen, so a period longer than
    /// that is unreachable. A rhythm the app cannot fold to is a rhythm nobody can find —
    /// this pins it to the DEFAULT range rather than one two taps away.</summary>
    [Fact]
    public void KiraSeizureRhythm_IsFoldableAtTheDefaultRange()
    {
        const int defaultRangeDays = 30;
        var pattern = DemoProfile.Kira(german: false).Seizures!;

        // Mirrors ConstellationViewModel.MaxPeriodDays.
        var maxFold = Math.Max(2, Math.Min(60, defaultRangeDays / 2));

        Assert.True(pattern.ClusterSpacingDays <= maxFold,
            $"a {pattern.ClusterSpacingDays}-day rhythm cannot be folded to at {defaultRangeDays} days (max {maxFold})");
    }

    // ── Mira: a different condition, and no invented seizures ───────────────────

    /// <summary>A cat that has never seized must never be given a seizure row to make the
    /// demo look fuller — the legend lists only what is present.</summary>
    [Fact]
    public void Mira_HasNoSeizuresAtAll()
    {
        Assert.Empty(Mira().Seizures);
        Assert.DoesNotContain(DemoProfile.Mira(german: false).Trackers, t => t.Id == TrackerId.Seizure);
    }

    /// <summary>Mira carries the Cycle story on glucose instead: two tight windows either
    /// side of the day, which fold into two arcs bracketing the insulin spokes. Proof the
    /// lens is not an epilepsy-only trick.</summary>
    [Fact]
    public void MiraGlucose_SitsInTwoWindowsAroundTheInsulinTimes()
    {
        var habit = DemoProfile.Mira(german: false).Glucose!;
        var readings = Mira().Glucose;

        Assert.NotEmpty(readings);
        Assert.All(readings, r =>
        {
            var h = r.Time.Hours;
            var morning = h >= habit.MorningWindow.From && h <= habit.MorningWindow.To;
            var evening = h >= habit.EveningWindow.From && h <= habit.EveningWindow.To;
            Assert.True(morning || evening, $"a reading at {r.Time} is in neither window");
        });

        // Both windows must actually be used, or it is one arc and the fold shows a spoke.
        Assert.Contains(readings, r => r.Time.Hours <= habit.MorningWindow.To);
        Assert.Contains(readings, r => r.Time.Hours >= habit.EveningWindow.From);
    }

    // ── The Cycle lens, folded at a week ────────────────────────────────────────

    /// <summary>
    /// The household-routine story: the last dose of the day runs late more often at the
    /// weekend, which a seven-day fold turns into a visible spoke. This is the one pattern
    /// an owner can actually act on, so it has to survive a parameter tweak being noticed.
    /// </summary>
    [Fact]
    public void KiraEveningDose_RunsLateMoreOftenOnFridayAndSaturday()
    {
        var med = DemoProfile.Kira(german: false).Medications[0];
        var lastTime = med.Times.Max();
        var logs = Kira().Medications[0].DoseLogs
            .Where(l => l.ScheduledTime == lastTime && l.ResolvedAt is not null)
            .ToList();

        static bool Late(MedicationDoseLog l) =>
            (l.ResolvedAt!.Value - l.ScheduledDate.Add(l.ScheduledTime)).TotalMinutes > 60;

        var weekend = logs.Where(l => l.ScheduledDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday).ToList();
        var weekday = logs.Except(weekend).ToList();

        var weekendRate = (double)weekend.Count(Late) / weekend.Count;
        var weekdayRate = (double)weekday.Count(Late) / weekday.Count;

        Assert.True(weekendRate > weekdayRate * 1.5,
            $"late-dose rate Fri/Sat {weekendRate:P0} vs rest {weekdayRate:P0} — no weekday spoke to fold to");
    }

    // ── The Nights lens: the wall thickens ──────────────────────────────────────

    /// <summary>A new owner records little and builds the habit, so the wall of nights gets
    /// denser as the eye travels down. It is one of the four questions that lens answers and
    /// the only one that needs a whole year to show.</summary>
    [Fact]
    public void Logging_GetsDenserAcrossTheYear()
    {
        var seed = Kira();
        var from = Today.AddDays(-(DemoProfile.Kira(german: false).HistoryDays - 1));
        var window = TimeSpan.FromDays(45);

        var early = CountEntriesBetween(seed, from, from + window);
        var late = CountEntriesBetween(seed, Today - window, Today);

        Assert.True(late > early * 1.3,
            $"{early} entries in the first 45 days vs {late} in the last — the wall never thickens");
    }

    // ── The quiet spell ─────────────────────────────────────────────────────────

    /// <summary>Life happens, and the app never scolds. The gap must be genuinely empty
    /// across EVERY signal — a fortnight that is blank for mood but full of doses reads as
    /// a glitch rather than a fortnight the owner had other things on.</summary>
    [Fact]
    public void TheQuietSpell_IsEmptyAcrossEverySignal()
    {
        var profile = DemoProfile.Kira(german: false);
        var seed = Kira();

        var start = Today.AddDays(-profile.QuietSpellStartDaysAgo);
        var end = start.AddDays(profile.QuietSpellLength);

        Assert.Equal(0, CountEntriesBetween(seed, start, end.AddDays(-1)));
        Assert.DoesNotContain(seed.Seizures, s => s.Date >= start && s.Date < end);
        Assert.DoesNotContain(
            seed.Medications.SelectMany(m => m.DoseLogs),
            l => l.ScheduledDate >= start && l.ScheduledDate < end);
    }

    // ── The vet report's headline ───────────────────────────────────────────────

    /// <summary>Mira's weight declines across the year — a signed, neutral fact, and the
    /// thing the report leads with. Compared as first-half against second-half average so a
    /// single jittered reading cannot flip the assertion.</summary>
    [Fact]
    public void MiraWeight_DeclinesAcrossTheYear()
    {
        var weights = Mira().Entries
            .Where(e => e.Weight > 0)
            .OrderBy(e => e.Date)
            .Select(e => e.Weight)
            .ToList();

        Assert.True(weights.Count >= 20, $"only {weights.Count} weigh-ins — too few to read as a line");

        var half = weights.Count / 2;
        Assert.True(weights.Take(half).Average() > weights.Skip(half).Average(),
            "the second half of the year is not lighter than the first");
    }

    // ── Both custom shapes, across the pair ─────────────────────────────────────

    /// <summary>Between them the two pets exercise both owner-defined shapes: an Amount with
    /// the owner's own unit, and a bare Tick. A demo that only ever showed one would leave
    /// half the feature — and half the report's layout — unfilmed.</summary>
    [Fact]
    public void TheTwoProfiles_CoverBothCustomTrackerShapes()
    {
        var kira = Kira().CustomTrackers.Single();
        var mira = Mira().CustomTrackers.Single();

        Assert.Equal(CustomShape.Amount, kira.Tracker.Shape);
        Assert.All(kira.Entries, e => Assert.NotNull(e.Amount));
        Assert.NotEmpty(kira.Tracker.Unit);

        Assert.Equal(CustomShape.Tick, mira.Tracker.Shape);
        Assert.All(mira.Entries, e => Assert.Null(e.Amount));
        Assert.Empty(mira.Tracker.Unit);
    }

    /// <summary>Only the owner says what belongs in front of a vet. The presets already
    /// encode the two obvious answers, and seeding must not quietly override them: a walk is
    /// life, a sick episode is medicine.</summary>
    [Fact]
    public void CustomTrackers_KeepTheirPresetsReportSetting()
    {
        Assert.False(Kira().CustomTrackers.Single().Tracker.IncludeInReport);
        Assert.True(Mira().CustomTrackers.Single().Tracker.IncludeInReport);
    }

    // ── Both appetite / water modes ─────────────────────────────────────────────

    /// <summary>The two water modes are separate stores and the report keeps two graphs.
    /// The demo has to fill both, or the separation is invisible in every screenshot.</summary>
    [Fact]
    public void MiraWater_UsesBothModes()
    {
        var seed = Mira();
        Assert.NotEmpty(seed.WaterAmounts);
        Assert.NotEmpty(seed.WaterLevels);
    }

    [Fact]
    public void Appetite_UsesBothModes()
    {
        var seed = Kira();
        Assert.NotEmpty(seed.AppetiteAmounts);
        Assert.NotEmpty(seed.AppetiteLevels);
    }

    // ── Re-shootability ─────────────────────────────────────────────────────────

    /// <summary>
    /// A creator who re-shoots a take tomorrow must get the identical sky, or the second
    /// take does not match the first.
    ///
    /// <para>This is also why the seed is FNV-1a over the profile id rather than
    /// <c>string.GetHashCode</c>, which is randomized per process — that alone would make
    /// every launch a different history while looking perfectly deterministic in a
    /// single-process test run.</para>
    /// </summary>
    [Fact]
    public void TheSameProfile_AlwaysBuildsTheSameHistory()
    {
        var a = Kira();
        var b = Kira();

        Assert.Equal(a.RowCount, b.RowCount);
        Assert.Equal(
            a.Seizures.Select(s => s.Date.Add(s.Time)),
            b.Seizures.Select(s => s.Date.Add(s.Time)));
        Assert.Equal(
            a.Glucose.Select(g => g.Value),
            b.Glucose.Select(g => g.Value));
    }

    [Fact]
    public void TheTwoProfiles_DoNotShareAHistory()
    {
        // Same generator, same year, different seed — two pets in one household must not
        // have interchangeable diaries any more than they have interchangeable skies.
        Assert.NotEqual(Kira().RowCount, Mira().RowCount);
    }

    // ── Scale ───────────────────────────────────────────────────────────────────

    /// <summary>A year has to land somewhere near a real diary: dense enough that the
    /// 365-day range reads as a life with a lot in it, small enough to insert in one
    /// transaction without a visible freeze.</summary>
    [Theory]
    [InlineData("kira")]
    [InlineData("mira")]
    public void AYearOfHistory_IsTheRightOrderOfMagnitude(string id)
    {
        var seed = Build(DemoProfile.Find(id, german: false)!);
        Assert.InRange(seed.RowCount, 1000, 8000);
    }

    /// <summary>Every dose log points at a real scheduled slot. A log whose time matches no
    /// schedule row is a dose the app can never reconcile — it would sit in the Journal
    /// forever as something that was neither given nor missed.</summary>
    [Theory]
    [InlineData("kira")]
    [InlineData("mira")]
    public void EveryDoseLog_MatchesOneOfItsMedicationsScheduledSlots(string id)
    {
        var seed = Build(DemoProfile.Find(id, german: false)!);

        foreach (var med in seed.Medications)
        {
            var slots = med.Schedules.Select(s => (s.Day, s.Time)).ToHashSet();
            Assert.All(med.DoseLogs, log =>
                Assert.Contains((log.ScheduledDate.DayOfWeek, log.ScheduledTime), slots));
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static int CountEntriesBetween(DemoSeed seed, DateTime from, DateTime to) =>
        seed.Entries.Count(e => e.Date >= from && e.Date <= to)
        + seed.Glucose.Count(g => g.Date >= from && g.Date <= to)
        + seed.AppetiteLevels.Count(a => a.Date >= from && a.Date <= to)
        + seed.AppetiteAmounts.Count(a => a.Date >= from && a.Date <= to)
        + seed.WaterAmounts.Count(w => w.Date >= from && w.Date <= to)
        + seed.WaterLevels.Count(w => w.Date >= from && w.Date <= to)
        + seed.CustomTrackers.Sum(c => c.Entries.Count(e => e.Date >= from && e.Date <= to));
}
