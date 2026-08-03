namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// The two rules behind Today's customizable stat cards: which pair a pet starts with,
/// and that one record can never occupy both cards.
///
/// <para>Both are pure, and both are the kind of thing that breaks silently — a bad
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
