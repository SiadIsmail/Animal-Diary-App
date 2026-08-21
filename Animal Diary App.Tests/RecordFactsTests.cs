namespace Animal_Diary_App.Tests;

using System.Reflection;
using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// Facts about the record: the arithmetic, not the wording.
///
/// <para>Every failure here is silent in the app: the panel still renders, with numbers
/// that quietly say something the owner never wrote down. A band boundary off by an hour
/// moves entries between "Night" and "Morning"; a one-per-day store counted by rows
/// inflates the count; a mean that crept in looks like the most useful number on the
/// screen and is the one thing this feature may never state.</para>
/// </summary>
public class RecordFactsTests
{
    private static readonly DateTime From = new(2026, 5, 22);
    private static readonly DateTime To = new(2026, 8, 19);

    private static readonly TodayCardKey Glucose = TodayCardId.Glucose;

    private static RecordMoment At(int day, int hour, int minute = 0, decimal? value = null) =>
        new(new DateTime(2026, 6, day, hour, minute, 0), value);

    private static RecordFacts Build(
        IEnumerable<RecordMoment>? events = null,
        IEnumerable<RecordMoment>? perDay = null,
        DoseCounts? doses = null)
        => RecordFactsBuilder.Build(Glucose, From, To,
            events ?? Array.Empty<RecordMoment>(),
            perDay ?? Array.Empty<RecordMoment>(),
            doses);

    // ── The four bands ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0, DayPart.Night)]
    [InlineData(5, 59, DayPart.Night)]
    [InlineData(6, 0, DayPart.Morning)]     // half-open: 06:00 belongs to Morning
    [InlineData(11, 59, DayPart.Morning)]
    [InlineData(12, 0, DayPart.Afternoon)]
    [InlineData(17, 59, DayPart.Afternoon)]
    [InlineData(18, 0, DayPart.Evening)]
    [InlineData(23, 59, DayPart.Evening)]
    public void Bands_AreHalfOpen_LowerBoundInclusive(int hour, int minute, DayPart expected)
        => Assert.Equal(expected, DayPartCounts.Of(new TimeSpan(hour, minute, 0)));

    [Fact]
    public void Bands_TotalToTheCount()
    {
        var facts = Build(events: new[]
        {
            At(1, 0), At(1, 5), At(2, 6), At(2, 11), At(3, 12), At(3, 17), At(4, 18), At(4, 23),
        });

        Assert.Equal(8, facts.Count);
        Assert.Equal(facts.Count, facts.DayParts.Total);
        Assert.Equal(new DayPartCounts(2, 2, 2, 2), facts.DayParts);
    }

    /// <summary>All four, always, in fixed order: an empty band is a zero that stays on
    /// screen. Dropping it, or leading with the busiest, would be the app choosing the
    /// finding instead of the owner seeing it.</summary>
    [Fact]
    public void Bands_KeepTheirZeros_AndTheirOrder()
    {
        var facts = Build(events: new[] { At(1, 2), At(2, 3), At(3, 4) });

        Assert.Equal(new DayPartCounts(3, 0, 0, 0), facts.DayParts);
        Assert.Equal(new[] { 3, 0, 0, 0 }, facts.DayParts.InOrder);
    }

    // ── Rows versus days ─────────────────────────────────────────────────────

    /// <summary>A one-per-day store counts DAYS. Counting its rows would inflate every
    /// count the moment a revived tombstone or a raced natural-key merge left two rows
    /// on one date, and nothing about the displayed number would look wrong.</summary>
    [Fact]
    public void OnePerDayStore_CountsDaysNotRows()
    {
        var facts = Build(perDay: new[]
        {
            At(1, 8, value: 7.6m),
            At(1, 20, value: 7.8m),   // same day, should not count twice
            At(2, 9, value: 7.7m),
        });

        Assert.Equal(2, facts.Count);
        Assert.Equal(facts.Count, facts.DayParts.Total);
    }

    /// <summary>The earliest moment of a day is the one that lands in a band, so a
    /// duplicate can never move the day into a different one either.</summary>
    [Fact]
    public void OnePerDayStore_BandsByTheEarliestMomentOfTheDay()
    {
        var facts = Build(perDay: new[] { At(1, 20), At(1, 8) });

        Assert.Equal(new DayPartCounts(0, 1, 0, 0), facts.DayParts);
    }

    [Fact]
    public void EventsAndPerDayStores_BothCountTowardsOneTotal()
    {
        var facts = Build(
            events: new[] { At(1, 8, value: 180m), At(1, 14, value: 120m) },
            perDay: new[] { At(1, 20) });

        Assert.Equal(3, facts.Count);
        Assert.Equal(facts.Count, facts.DayParts.Total);
    }

    // ── Where the day-part row appears ───────────────────────────────────────
    //
    // Day-parts are stated where the entry's TIME IS A FACT ABOUT THE PET and suppressed
    // where it is a fact about the owner's routine. Getting this wrong is silent: the row
    // simply appears (or does not) with numbers that describe when someone picks up their
    // phone. The counts themselves are unchanged either way: only whether they are worth
    // stating. See Data/Models/RecordFacts.cs.

    [Fact]
    public void AnEventStore_StatesItsDayParts()
    {
        Assert.True(Build(events: new[] { At(1, 7), At(1, 20) }).StatesDayParts);
    }

    [Fact]
    public void AOnePerDayStore_DoesNot()
    {
        Assert.False(Build(perDay: new[] { At(1, 8, value: 5.19m), At(2, 9, value: 5.2m) }).StatesDayParts);
    }

    /// <summary>Morning versus evening is the POINT of a dose, so doses state the row
    /// even though nothing about them is a reading.</summary>
    [Fact]
    public void Doses_StateTheirDayParts()
    {
        var facts = Build(events: new[] { At(1, 8), At(1, 20) }, doses: new DoseCounts(2, 0, 0));

        Assert.True(facts.StatesDayParts);
    }

    /// <summary>Water and appetite hold both shapes. As soon as there are measured
    /// events, the record genuinely has entries whose moment is a datum.</summary>
    [Fact]
    public void ARecordWithBothShapes_StatesTheRowOnceItHasEvents()
    {
        Assert.True(Build(
            events: new[] { At(1, 8, value: 180m) },
            perDay: new[] { At(1, 20) }).StatesDayParts);

        Assert.False(Build(perDay: new[] { At(1, 20) }).StatesDayParts);
    }

    // ── Nothing written down ─────────────────────────────────────────────────

    /// <summary>A zero-count snapshot, never null: "you wrote nothing down" is itself a
    /// fact about the record, and a null would make every caller grow a second path.</summary>
    [Fact]
    public void EmptyRange_IsAZeroCountSnapshot()
    {
        var facts = Build();

        Assert.NotNull(facts);
        Assert.False(facts.HasAny);
        Assert.Equal(0, facts.Count);
        Assert.Equal(default, facts.DayParts);
        Assert.Null(facts.Lowest);
        Assert.Null(facts.Highest);
        Assert.Null(facts.FirstOn);
        Assert.Null(facts.LastOn);
    }

    /// <summary>An empty range still carries the dose counts: a period where every dose
    /// went unanswered is not the same as one with no medication in it.</summary>
    [Fact]
    public void EmptyRange_StillCarriesDoseCounts()
    {
        var facts = Build(doses: new DoseCounts(0, 0, 4));

        Assert.False(facts.HasAny);
        Assert.Equal(new DoseCounts(0, 0, 4), facts.Doses);
    }

    // ── The numbers ──────────────────────────────────────────────────────────

    [Fact]
    public void LowestHighestAndLatest_ComeFromTheRecordedValues()
    {
        var facts = Build(events: new[]
        {
            At(1, 8, value: 14.2m),
            At(2, 9, value: 3.1m),
            At(3, 7, value: 22.4m),
            At(4, 6, value: 9.9m),
        });

        Assert.Equal(3.1m, facts.Lowest);
        Assert.Equal(22.4m, facts.Highest);
        Assert.Equal(9.9m, facts.Latest);
        Assert.Equal(new DateTime(2026, 6, 4), facts.LatestOn);
        Assert.Equal(new DateTime(2026, 6, 1), facts.FirstOn);
        Assert.Equal(new DateTime(2026, 6, 4), facts.LastOn);
    }

    /// <summary>Moments can arrive in any order: the stores are queried without one.</summary>
    [Fact]
    public void OrderOfArrival_DoesNotChangeAnything()
    {
        var ascending = Build(events: new[] { At(1, 8, value: 5m), At(3, 8, value: 9m) });
        var descending = Build(events: new[] { At(3, 8, value: 9m), At(1, 8, value: 5m) });

        Assert.Equal(ascending, descending);
    }

    /// <summary>A record with no numbers (a seizure, a mood, a Tick tracker) reports
    /// counts and dates and nothing else. Lowest/Highest on a qualitative record would
    /// mean an observation had been turned into a number.</summary>
    [Fact]
    public void QualitativeRecord_HasCountsButNoValues()
    {
        var facts = Build(events: new[] { At(1, 3), At(9, 3), At(18, 22) });

        Assert.Equal(3, facts.Count);
        Assert.False(facts.HasValues);
        Assert.Null(facts.Lowest);
        Assert.Null(facts.Highest);
        Assert.Null(facts.Latest);
    }

    /// <summary>The latest NUMBER, not the latest moment: a two-store record whose most
    /// recent entry was an observation still reports the last measurement it holds.</summary>
    [Fact]
    public void Latest_IsTheLastValue_NotTheLastMoment()
    {
        var facts = Build(
            events: new[] { At(1, 8, value: 180m) },
            perDay: new[] { At(5, 9) });

        Assert.Equal(180m, facts.Latest);
        Assert.Equal(new DateTime(2026, 6, 1), facts.LatestOn);
        Assert.Equal(new DateTime(2026, 6, 5), facts.LastOn);
    }

    // ── The line that must not be crossed ────────────────────────────────────

    /// <summary>
    /// Nothing in this read model averages, trends, ranks or counts a run of days.
    ///
    /// <para>A ratchet, not a proof: it cannot see inside a method body. What it does
    /// catch is the way this actually gets reintroduced: someone adds
    /// <c>AverageValue</c> or <c>DaysSince</c> because a screen wanted it, and every
    /// review after that treats it as part of the shape. An average of readings taken at
    /// irregular times is a number that looks meaningful and is not; a streak is
    /// something the owner can break by writing down the truth. Neither may exist here
    /// (AI/domain.md, AI/design-decisions.md).</para>
    /// </summary>
    [Theory]
    [InlineData(typeof(RecordFacts))]
    [InlineData(typeof(DayPartCounts))]
    [InlineData(typeof(DoseCounts))]
    public void TheReadModel_NamesNothingItIsNotAllowedToState(Type type)
    {
        string[] banned =
        {
            "Average", "Mean", "Avg", "Median", "Trend", "Delta", "Change",
            "Streak", "DaysSince", "Percent", "Rate", "Score", "Adherence",
        };

        var offenders = type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(name => banned.Any(b => name.Contains(b, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(offenders);
    }
}
