namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// The two rules behind Today's customizable stat cards: which pair a pet starts with,
/// and that one record can never occupy both cards.
///
/// <para>Both are pure, and both are the kind of thing that breaks silently: a bad
/// default shows an epileptic dog's owner a weigh-in instead of the last seizure, and a
/// duplicate pair turns two cards into one fact shown twice.</para>
/// </summary>
public class TodayCardCatalogTests
{
    // ── Defaults from the pet's conditions ───────────────────────────────────

    [Fact]
    public void NoCondition_DefaultsToWeightAndMood()
    {
        var config = TodayCardCatalog.DefaultsFor(null);

        Assert.Equal(TodayCardId.Weight, config.Primary);
        Assert.Equal(TodayCardId.Mood, config.Secondary);
    }

    [Fact]
    public void EmptyConditionIds_AreIgnored()
    {
        // "" is the catalog's "None / Not sure" id, and a pet can carry blanks.
        var config = TodayCardCatalog.DefaultsFor(new string?[] { "", null });

        Assert.Equal(TodayCardId.Weight, config.Primary);
        Assert.Equal(TodayCardId.Mood, config.Secondary);
    }

    [Fact]
    public void Epilepsy_ShowsTheLastSeizureAndTheLastDose()
    {
        var config = TodayCardCatalog.DefaultsFor(new[] { "epilepsy" });

        Assert.Equal(TodayCardId.Seizure, config.Primary);
        Assert.Equal(TodayCardId.Medication, config.Secondary);
    }

    [Fact]
    public void Diabetes_LeadsWithGlucose()
    {
        var config = TodayCardCatalog.DefaultsFor(new[] { "diabetes" });

        Assert.Equal(TodayCardId.Glucose, config.Primary);
        Assert.Equal(TodayCardId.Weight, config.Secondary);
    }

    [Fact]
    public void TwoConditions_TakeOneCardEach()
    {
        // Not both of the first condition's cards: a pet with two conditions has two
        // things worth watching, one from each.
        var config = TodayCardCatalog.DefaultsFor(new[] { "diabetes", "epilepsy" });

        Assert.Equal(TodayCardId.Glucose, config.Primary);
        Assert.Equal(TodayCardId.Seizure, config.Secondary);
    }

    [Fact]
    public void UnknownCondition_FallsBackToTheGeneralPair()
    {
        var config = TodayCardCatalog.DefaultsFor(new[] { "something-added-later" });

        Assert.Equal(TodayCardId.Weight, config.Primary);
        Assert.Equal(TodayCardId.Mood, config.Secondary);
    }

    [Fact]
    public void DefaultsAreNeverTheSameCardTwice()
    {
        foreach (var condition in new string?[] { null, "", "diabetes", "ckd", "epilepsy" })
        {
            var config = TodayCardCatalog.DefaultsFor(new[] { condition });
            Assert.NotEqual(config.Primary, config.Secondary);
        }
    }

    // ── Owner-defined trackers as cards ──────────────────────────────────────

    [Fact]
    public void TwoCustomTrackers_AreDifferentCards()
    {
        // The reason TodayCardKey exists. With a single TodayCardId.Custom these two
        // would compare equal, so putting "Walk" on the left card would light "Vomiting"
        // up as already-chosen and the swap rule would fire between unrelated records.
        var walk = TodayCardKey.Custom(1);
        var vomiting = TodayCardKey.Custom(2);

        var config = new TodayCardConfig(walk, vomiting).With(TodayCardSlot.Primary, TodayCardId.Mood);

        Assert.Equal<TodayCardKey>(TodayCardId.Mood, config.Primary);
        Assert.Equal(vomiting, config.Secondary);   // untouched, not swapped away
        Assert.NotEqual(walk, vomiting);
    }

    [Fact]
    public void ACustomCardSwapsWithABuiltInOne_LikeAnyOtherPair()
    {
        var walk = TodayCardKey.Custom(7);
        var config = new TodayCardConfig(TodayCardId.Weight, walk)
            .With(TodayCardSlot.Primary, walk);

        Assert.Equal(walk, config.Primary);
        Assert.Equal<TodayCardKey>(TodayCardId.Weight, config.Secondary);
    }

    [Theory]
    [InlineData("Weight")]
    [InlineData("Medication")]
    [InlineData("custom:1")]
    [InlineData("custom:4096")]
    public void AStoredKeyRoundTrips(string stored)
    {
        // Built-in forms are unchanged from before custom cards existed, so a preference
        // written by an older build still parses instead of silently resetting the pair.
        Assert.True(TodayCardKey.TryParse(stored, out var key));
        Assert.Equal(stored, key.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Nonsense")]        // a card removed in a later version
    [InlineData("custom:")]         // truncated write
    [InlineData("custom:0")]        // no row can have id 0
    [InlineData("custom:-3")]
    [InlineData("custom:abc")]
    public void AnUnparseableKeyFails_SoTheCallerCanFallBackToDefaults(string? stored)
    {
        Assert.False(TodayCardKey.TryParse(stored, out _));
    }

    // ── Choosing a card ──────────────────────────────────────────────────────

    [Fact]
    public void PickingAFreeRecord_ReplacesOnlyThatCard()
    {
        var config = new TodayCardConfig(TodayCardId.Weight, TodayCardId.Mood)
            .With(TodayCardSlot.Primary, TodayCardId.Water);

        Assert.Equal(TodayCardId.Water, config.Primary);
        Assert.Equal(TodayCardId.Mood, config.Secondary);
    }

    [Fact]
    public void PickingWhatTheOtherCardShows_SwapsThemInsteadOfDuplicating()
    {
        var config = new TodayCardConfig(TodayCardId.Weight, TodayCardId.Mood)
            .With(TodayCardSlot.Primary, TodayCardId.Mood);

        Assert.Equal(TodayCardId.Mood, config.Primary);
        Assert.Equal(TodayCardId.Weight, config.Secondary);
    }

    [Fact]
    public void PickingWhatThisCardAlreadyShows_ChangesNothing()
    {
        var config = new TodayCardConfig(TodayCardId.Weight, TodayCardId.Mood)
            .With(TodayCardSlot.Secondary, TodayCardId.Mood);

        Assert.Equal(TodayCardId.Weight, config.Primary);
        Assert.Equal(TodayCardId.Mood, config.Secondary);
    }

    [Fact]
    public void NoPickCanEverPutOneRecordOnBothCards()
    {
        var start = new TodayCardConfig(TodayCardId.Weight, TodayCardId.Mood);

        foreach (var meta in TodayCardCatalog.Cards)
        {
            foreach (var slot in new[] { TodayCardSlot.Primary, TodayCardSlot.Secondary })
            {
                var config = start.With(slot, meta.Id);
                Assert.Equal(meta.Id, config.For(slot));
                Assert.NotEqual(config.Primary, config.Secondary);
            }
        }
    }

    // ── The table itself ─────────────────────────────────────────────────────

    [Fact]
    public void EveryCardIdHasARow()
    {
        // A card missing from the table would silently render as the first one.
        foreach (TodayCardId id in Enum.GetValues<TodayCardId>())
            Assert.Equal(id, TodayCardCatalog.Meta(id).Id);
    }

    [Fact]
    public void OnlyMedicationIsNotATracker()
    {
        // Doses live in the medication model, never as a Tracker; every other card
        // reads from a tracker the Journal already collects.
        foreach (var meta in TodayCardCatalog.Cards)
        {
            if (meta.Id == TodayCardId.Medication)
                Assert.Null(meta.Tracker);
            else
                Assert.NotNull(meta.Tracker);
        }
    }
}
