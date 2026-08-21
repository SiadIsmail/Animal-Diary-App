using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

namespace Animal_Diary_App.Tests;

/// <summary>
/// "What is still to do for this pet today?": the rules behind the Journal's chip row
/// and the Today page's care ring.
///
/// <para>Worth testing precisely because it is the app's one piece of care logic that
/// no server checks: if it says a dose is done when it isn't, the chip disappears and
/// nobody is reminded. The engine is pure, so every rule below is a plain function
/// call with no database and no clock.</para>
/// </summary>
public class PendingEngineTests
{
    private static readonly DateTime Today = new(2026, 7, 15);

    private static CarePlanItem Track(TrackerKey key, TrackerKind kind, int perDay = 0) =>
        new() { Key = key, Kind = kind, PerDayCount = perDay };

    private static ScheduledDose Dose(string name, int hour, bool given) =>
        new(MedicationId: name.GetHashCode(), PetId: 1, MedicationName: name,
            Time: TimeSpan.FromHours(hour), Given: given);

    private static Dictionary<TrackerKey, IReadOnlyList<DateTime>> Entries(
        params (TrackerKey Key, DateTime[] Dates)[] rows) =>
        rows.ToDictionary(r => r.Key, r => (IReadOnlyList<DateTime>)r.Dates);

    private static IReadOnlyList<PendingItem> Compute(
        IReadOnlyList<CarePlanItem>? plan = null,
        IReadOnlyList<ScheduledDose>? doses = null,
        Dictionary<TrackerKey, IReadOnlyList<DateTime>>? entries = null) =>
        PendingEngine.Compute(
            plan ?? Array.Empty<CarePlanItem>(),
            doses ?? Array.Empty<ScheduledDose>(),
            entries ?? new Dictionary<TrackerKey, IReadOnlyList<DateTime>>(),
            Today);

    // ── Medication doses ─────────────────────────────────────────────────────────

    [Fact]
    public void UngivenDosesComeFirst_SoonestFirst_AndGivenOnesAreGone()
    {
        var result = Compute(doses: new[]
        {
            Dose("Evening", 18, given: false),
            Dose("Midday", 12, given: true),
            Dose("Morning", 8, given: false),
        });

        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal(PendingKind.Medication, i.Kind));
        Assert.Equal("Morning", result[0].MedicationName);
        Assert.Equal("Evening", result[1].MedicationName);
    }

    [Fact]
    public void AllOfTodaysUngivenDosesShow_EvenOnesNotYetDue()
    {
        // Deliberate: the chip row is the day's plan, not a countdown. A dose at 23:00
        // is listed at breakfast so the day reads whole.
        var result = Compute(doses: new[] { Dose("Late", 23, given: false) });

        Assert.Single(result);
    }

    [Fact]
    public void DosesAtTheSameTime_AreOrderedByNameSoTheListIsStable()
    {
        var result = Compute(doses: new[]
        {
            Dose("Vetmedin", 8, given: false),
            Dose("Amlodipine", 8, given: false),
        });

        Assert.Equal("Amlodipine", result[0].MedicationName);
        Assert.Equal("Vetmedin", result[1].MedicationName);
    }

    // ── Tracker cadences ─────────────────────────────────────────────────────────

    [Fact]
    public void PerDay_IsPendingUntilTheCountIsMet_AndCarriesTheProgress()
    {
        var plan = new[] { Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 3) };
        var entries = Entries((TrackerId.Glucose, new[] { Today, Today }));

        var result = Compute(plan, entries: entries);

        var item = Assert.Single(result);
        Assert.Equal(TrackerId.Glucose, item.Tracker?.BuiltIn);
        Assert.Equal(2, item.Done);
        Assert.Equal(3, item.Target);
    }

    [Fact]
    public void PerDay_ClearsOnceTheCountIsReached()
    {
        var plan = new[] { Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 2) };
        var entries = Entries((TrackerId.Glucose, new[] { Today, Today }));

        Assert.Empty(Compute(plan, entries: entries));
    }

    [Fact]
    public void PerDay_CountsOnlyToday_NotYesterdaysReadings()
    {
        var plan = new[] { Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 1) };
        var entries = Entries((TrackerId.Glucose, new[] { Today.AddDays(-1) }));

        Assert.Single(Compute(plan, entries: entries));
    }

    [Theory]
    // Daily: today only.
    [InlineData(TrackerKind.Daily, 0, false)]
    [InlineData(TrackerKind.Daily, -1, true)]
    // TwiceWeekly: a rolling 3-day window (today and the previous two).
    [InlineData(TrackerKind.TwiceWeekly, -2, false)]
    [InlineData(TrackerKind.TwiceWeekly, -3, true)]
    // Weekly: a rolling 7-day window (today and the previous six).
    [InlineData(TrackerKind.Weekly, -6, false)]
    [InlineData(TrackerKind.Weekly, -7, true)]
    public void RollingWindows_AreInclusiveOfTodayAndExclusiveOnePastTheEdge(
        TrackerKind kind, int daysAgo, bool expectedPending)
    {
        var plan = new[] { Track(TrackerId.Weight, kind) };
        var entries = Entries((TrackerId.Weight, new[] { Today.AddDays(daysAgo) }));

        Assert.Equal(expectedPending, Compute(plan, entries: entries).Count == 1);
    }

    [Theory]
    [InlineData(TrackerKind.AsNeeded)]
    [InlineData(TrackerKind.Event)]
    public void AsNeededAndEvent_AreNeverPending(TrackerKind kind)
    {
        // A seizure log is Event: the app must never ask someone to have one.
        var plan = new[] { Track(TrackerId.Seizure, kind) };

        Assert.Empty(Compute(plan));
    }

    [Fact]
    public void AnEntryWithATimeOfDay_StillCountsForItsDay()
    {
        // Callers pass raw timestamps; the engine compares by date, not by instant.
        var plan = new[] { Track(TrackerId.Mood, TrackerKind.Daily) };
        var entries = Entries((TrackerId.Mood, new[] { Today.AddHours(21).AddMinutes(37) }));

        Assert.Empty(Compute(plan, entries: entries));
    }

    [Fact]
    public void MissingTrackerKey_ReadsAsNothingLogged_NotAsAnError()
    {
        var plan = new[] { Track(TrackerId.Appetite, TrackerKind.Daily) };

        Assert.Single(Compute(plan));
    }

    // ── Owner-created trackers ───────────────────────────────────────────────────

    [Fact]
    public void TwoCustomTrackers_AreCountedSeparately_NotAsOne()
    {
        // The whole reason TrackerKey exists. Were "custom" a single TrackerId member,
        // both of these would read the same dictionary entry, and writing down the walk
        // would quietly tick the groom off the day as well.
        var walk = TrackerKey.Custom(1);
        var groom = TrackerKey.Custom(2);
        var plan = new[] { Track(walk, TrackerKind.Daily), Track(groom, TrackerKind.Daily) };

        var result = Compute(plan, entries: Entries((walk, new[] { Today })));

        var item = Assert.Single(result);
        Assert.Equal(groom, item.Tracker);
    }

    [Fact]
    public void ACustomTrackerNeverSharesAKeyWithAShippedOne()
    {
        var plan = new[]
        {
            Track(TrackerId.Mood, TrackerKind.Daily),
            Track(TrackerKey.Custom(1), TrackerKind.Daily),
        };

        var result = Compute(plan, entries: Entries((TrackerId.Mood, new[] { Today })));

        var item = Assert.Single(result);
        Assert.True(item.Tracker?.IsCustom);
    }

    [Fact]
    public void Ordering_IsDosesThenPerDayThenTheRestInCarePlanOrder()
    {
        var plan = new[]
        {
            Track(TrackerId.Weight, TrackerKind.Weekly),
            Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 1),
            Track(TrackerId.Mood, TrackerKind.Daily),
        };

        var result = Compute(plan, doses: new[] { Dose("Insulin", 8, given: false) });

        Assert.Equal(PendingKind.Medication, result[0].Kind);
        Assert.Equal(TrackerId.Glucose, result[1].Tracker?.BuiltIn);   // PerDay is promoted
        Assert.Equal(TrackerId.Weight, result[2].Tracker?.BuiltIn);    // then care-plan order
        Assert.Equal(TrackerId.Mood, result[3].Tracker?.BuiltIn);
    }

    // ── The ring and the chips must never disagree ───────────────────────────────

    [Fact]
    public void ProgressIsComplete_ExactlyWhenNothingIsPending()
    {
        // The stated invariant on ComputeProgress. If these two ever drift, the Today
        // ring reads "all done" while the Journal still lists an unlogged dose.
        var plan = new[]
        {
            Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 2),
            Track(TrackerId.Mood, TrackerKind.Daily),
            Track(TrackerId.Weight, TrackerKind.Weekly),
            Track(TrackerId.Seizure, TrackerKind.Event),
        };
        var doses = new[] { Dose("Insulin", 8, given: true), Dose("Insulin", 20, given: false) };

        foreach (var glucoseToday in new[] { 0, 1, 2, 3 })
        foreach (var moodLogged in new[] { false, true })
        {
            var entries = Entries(
                (TrackerId.Glucose, Enumerable.Repeat(Today, glucoseToday).ToArray()),
                (TrackerId.Mood, moodLogged ? new[] { Today } : Array.Empty<DateTime>()),
                (TrackerId.Weight, new[] { Today.AddDays(-2) }));

            var pending = PendingEngine.Compute(plan, doses, entries, Today);
            var progress = PendingEngine.ComputeProgress(plan, doses, entries, Today);

            Assert.Equal(pending.Count == 0, progress.Done == progress.Total);
        }
    }

    [Fact]
    public void Progress_NeverExceedsTheTarget_WhenSomeoneLogsExtras()
    {
        // Four glucose readings against a target of three is care, not 133% of a ring.
        var plan = new[] { Track(TrackerId.Glucose, TrackerKind.PerDay, perDay: 3) };
        var entries = Entries((TrackerId.Glucose, Enumerable.Repeat(Today, 4).ToArray()));

        var progress = PendingEngine.ComputeProgress(
            plan, Array.Empty<ScheduledDose>(), entries, Today);

        Assert.Equal(3, progress.Total);
        Assert.Equal(3, progress.Done);
    }

    [Fact]
    public void Progress_CountsEventAndAsNeededTrackersNowhere()
    {
        var plan = new[]
        {
            Track(TrackerId.Seizure, TrackerKind.Event),
            Track(TrackerId.Water, TrackerKind.AsNeeded),
        };

        var progress = PendingEngine.ComputeProgress(
            plan, Array.Empty<ScheduledDose>(),
            new Dictionary<TrackerKey, IReadOnlyList<DateTime>>(), Today);

        Assert.Equal(0, progress.Total);
        Assert.Equal(0, progress.Done);
    }

    [Fact]
    public void EmptyPlanAndNoDoses_IsCompleteNotEmptyOfMeaning()
    {
        var progress = PendingEngine.ComputeProgress(
            Array.Empty<CarePlanItem>(), Array.Empty<ScheduledDose>(),
            new Dictionary<TrackerKey, IReadOnlyList<DateTime>>(), Today);

        Assert.Equal(0, progress.Total);
        Assert.Empty(Compute());
    }
}
