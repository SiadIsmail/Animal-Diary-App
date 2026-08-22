namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// Which unit an owner sees, derived from the entries they actually wrote.
///
/// <para><b>Why it is derived at all.</b> The alternative is a settings screen, which
/// means a migration prompt, a "choose your units" step in onboarding, and a preference
/// that can disagree with the diary. This codebase already prefers a derived answer to a
/// stored one (<c>Pet.AgeYears</c>, <c>TodayCardCatalog.DefaultsFor</c>, <c>VetVisit</c>'s
/// past-ness), and the derived answer here is simply better: an owner who starts logging
/// in pounds gets pounds, everywhere, without being asked.</para>
///
/// <para>Every rule below is silent when wrong. Nothing crashes; the app just shows a
/// number in a unit the owner did not choose, next to a number in a unit they did.</para>
/// </summary>
public class DisplayUnitResolverTests
{
    private static readonly DateTime Jan = new(2026, 1, 1);

    private static UnitTally Tally(string unitId, int count, int dayOffset = 0)
        => new(unitId, count, Jan.AddDays(dayOffset));

    // ── Rule 1: the majority ─────────────────────────────────────────────────

    [Fact]
    public void The_unit_most_entries_were_written_in_wins()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("lb", 10), Tally("kg", 1, 5) },
            remembered: null,
            regionCode: "DE");

        Assert.Equal("lb", unit.Id);
    }

    /// <summary>The majority beats both fallbacks, and it beats them even when they
    /// agree with each other: what the owner wrote down outranks what they last tapped
    /// and outranks where their phone thinks it is.</summary>
    [Fact]
    public void The_majority_outranks_the_remembered_pick_and_the_locale()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("lb", 3) },
            remembered: "kg",
            regionCode: "DE");

        Assert.Equal("lb", unit.Id);
    }

    /// <summary>Legacy rows arrive tallied under the canonical id (that is what "no
    /// unit" means), so a history written entirely before units existed resolves to
    /// kilograms and nothing on screen changes for that owner.</summary>
    [Fact]
    public void A_history_written_before_units_existed_stays_canonical()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("kg", 42) },
            remembered: "lb",
            regionCode: "US");

        Assert.Equal("kg", unit.Id);
    }

    // ── Rule 2: a tie goes to the most recent ────────────────────────────────

    [Fact]
    public void A_tie_resolves_to_whichever_was_used_most_recently()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("kg", 5, dayOffset: 10), Tally("lb", 5, dayOffset: 40) },
            remembered: null,
            regionCode: "DE");

        Assert.Equal("lb", unit.Id);
    }

    [Fact]
    public void The_tie_break_is_symmetric()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("lb", 5, dayOffset: 40), Tally("kg", 5, dayOffset: 60) },
            remembered: null,
            regionCode: "US");

        Assert.Equal("kg", unit.Id);
    }

    /// <summary>The tie-break runs only among the units tied at the top. A unit with
    /// fewer entries cannot win by being the most recent: one stray gram entry after a
    /// year of kilograms must not relabel the whole chart.</summary>
    [Fact]
    public void A_recent_minority_entry_does_not_win()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("kg", 20, dayOffset: 5), Tally("g", 1, dayOffset: 400) },
            remembered: null,
            regionCode: "DE");

        Assert.Equal("kg", unit.Id);
    }

    /// <summary>Stamps are gathered per DAY, so two units last used on the same day are
    /// separated by nothing in the data. The answer still has to be the same every time
    /// the page loads, so it falls to the canonical unit rather than to whichever row
    /// SQLite happened to group first.</summary>
    [Fact]
    public void A_tie_on_the_same_day_is_still_deterministic()
    {
        var oneWay = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("lb", 5, dayOffset: 3), Tally("kg", 5, dayOffset: 3) },
            remembered: null, regionCode: "US");

        var otherWay = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("kg", 5, dayOffset: 3), Tally("lb", 5, dayOffset: 3) },
            remembered: null, regionCode: "US");

        Assert.Equal(oneWay.Id, otherWay.Id);
        Assert.Equal("kg", oneWay.Id);
    }

    // ── Rules 3 and 4: nothing logged yet ────────────────────────────────────

    [Fact]
    public void With_no_entries_the_remembered_pick_prefills()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            Array.Empty<UnitTally>(),
            remembered: "lb",
            regionCode: "DE");

        Assert.Equal("lb", unit.Id);
    }

    [Fact]
    public void With_no_entries_and_no_memory_the_locale_guesses()
    {
        Assert.Equal("lb", DisplayUnitResolver.Resolve(
            UnitFamily.Weight, Array.Empty<UnitTally>(), null, "US").Id);

        Assert.Equal("kg", DisplayUnitResolver.Resolve(
            UnitFamily.Weight, Array.Empty<UnitTally>(), null, "DE").Id);
    }

    /// <summary>A remembered value from another family, or one this build dropped, is
    /// ignored rather than resolving to canonical-by-accident: the locale guess is a
    /// better answer than a preference that no longer means anything.</summary>
    [Fact]
    public void A_remembered_pick_that_is_not_a_unit_of_this_family_is_ignored()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            Array.Empty<UnitTally>(),
            remembered: "mg_dl",
            regionCode: "US");

        Assert.Equal("lb", unit.Id);
    }

    [Fact]
    public void Tallies_with_no_entries_in_them_do_not_vote()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Glucose,
            new[] { Tally("mg_dl", 0) },
            remembered: "mmol_l",
            regionCode: "US");

        Assert.Equal("mmol_l", unit.Id);
    }

    /// <summary>A unit id this build does not recognise cannot win the vote. The stored
    /// number is canonical whatever the label says, so letting an unknown id win would
    /// mean rendering canonical numbers under someone else's unit name.</summary>
    [Fact]
    public void An_unknown_unit_cannot_win_the_majority()
    {
        var unit = DisplayUnitResolver.Resolve(
            UnitFamily.Weight,
            new[] { Tally("stones", 99), Tally("kg", 2) },
            remembered: null,
            regionCode: "DE");

        Assert.Equal("kg", unit.Id);
    }

    // ── Per (pet, record), not per pet ───────────────────────────────────────

    /// <summary>A US owner logs weight in pounds and glucose in mg/dL. Deriving each
    /// record independently gets both right with no extra concept and no screen; one
    /// "units" preference per pet would force one of the two to be wrong.</summary>
    [Fact]
    public void Two_records_for_one_pet_resolve_independently()
    {
        var weight = DisplayUnitResolver.Resolve(
            UnitFamily.Weight, new[] { Tally("lb", 8) }, null, "US");
        var glucose = DisplayUnitResolver.Resolve(
            UnitFamily.Glucose, new[] { Tally("mg_dl", 40) }, null, "US");

        Assert.Equal("lb", weight.Id);
        Assert.Equal("mg_dl", glucose.Id);
    }

    // ── Tallying ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tallying_counts_a_missing_unit_as_canonical()
    {
        var tallies = DisplayUnitResolver.Tally(UnitFamily.Weight, new (string?, DateTime)[]
        {
            (null, Jan),
            ("", Jan.AddDays(1)),
            ("lb", Jan.AddDays(2)),
        });

        var kg = Assert.Single(tallies, t => t.UnitId == "kg");
        Assert.Equal(2, kg.Count);
        Assert.Equal(Jan.AddDays(1), kg.LastAt);

        var lb = Assert.Single(tallies, t => t.UnitId == "lb");
        Assert.Equal(1, lb.Count);
    }

    [Fact]
    public void Tallying_keeps_the_latest_stamp_per_unit_whatever_order_rows_arrive_in()
    {
        var tallies = DisplayUnitResolver.Tally(UnitFamily.Volume, new (string?, DateTime)[]
        {
            ("cup", Jan.AddDays(30)),
            ("cup", Jan.AddDays(2)),
            ("cup", Jan.AddDays(11)),
        });

        var cup = Assert.Single(tallies);
        Assert.Equal(3, cup.Count);
        Assert.Equal(Jan.AddDays(30), cup.LastAt);
    }

    [Fact]
    public void Tallying_nothing_yields_nothing()
        => Assert.Empty(DisplayUnitResolver.Tally(
            UnitFamily.FoodMass, Array.Empty<(string?, DateTime)>()));
}
