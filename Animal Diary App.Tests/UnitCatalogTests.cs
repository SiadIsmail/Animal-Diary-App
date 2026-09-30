namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// The conversion half of per-entry units.
///
/// <para><b>What is actually being guarded.</b> The stored value is canonical and the
/// entered value is not, so between the owner's keyboard and their screen sits a
/// multiply and a divide. If those two do not compose exactly, the app quietly rewrites
/// a medical number: 11.4 lb comes back as 11.39, and it drifts again on the next edit.
/// Nothing in the toolchain notices, and the person it happens to is looking at a
/// weight chart deciding whether their cat is losing weight.</para>
///
/// <para>Glucose is the sharp end. mmol/L and mg/dL differ by a factor of eighteen, so
/// there is no such thing as being slightly wrong: 7.6 and 137 are the same reading and
/// 7.6 shown as mg/dL is a hypoglycaemic emergency that is not happening.</para>
/// </summary>
public class UnitCatalogTests
{
    // ── The table itself ─────────────────────────────────────────────────────

    [Fact]
    public void Every_family_declares_exactly_one_canonical()
    {
        foreach (UnitFamily family in Enum.GetValues<UnitFamily>())
        {
            var canonical = UnitCatalog.ForFamily(family).Where(u => u.IsCanonical).ToList();
            Assert.True(canonical.Count == 1,
                $"{family} declares {canonical.Count} canonical units; it must declare exactly one.");
            Assert.Equal(1m, canonical[0].ToCanonical);
        }
    }

    [Fact]
    public void Ids_are_unique_within_a_family()
    {
        foreach (UnitFamily family in Enum.GetValues<UnitFamily>())
        {
            var ids = UnitCatalog.ForFamily(family).Select(u => u.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>Grams is a weight unit AND a food-mass unit, with different factors,
    /// because the two families have different canonicals. That is exactly why lookup
    /// is family-scoped, and this pins the collision so nobody "fixes" it by adding a
    /// bare <c>Get(id)</c> that would resolve one of them to the wrong factor.</summary>
    [Fact]
    public void Grams_means_different_things_in_the_two_mass_families()
    {
        Assert.Equal(0.001m, UnitCatalog.Get(UnitFamily.Weight, "g").ToCanonical);
        Assert.Equal(1m, UnitCatalog.Get(UnitFamily.FoodMass, "g").ToCanonical);
    }

    [Fact]
    public void Every_offered_unit_is_reachable_by_its_stored_id()
    {
        foreach (var unit in UnitCatalog.All)
        {
            Assert.True(UnitCatalog.IsKnown(unit.Family, unit.Id));
            Assert.Equal(unit, UnitCatalog.Get(unit.Family, unit.Id));
        }
    }

    /// <summary>Appetite offers grams and ounces and nothing else. A cup of dry food is
    /// a VOLUME and this family is a MASS, so the conversion depends on the food's
    /// density and any factor is wrong for most foods. Cups for water are fine and are
    /// tested below; this is the one that must stay absent, however often it is asked
    /// for.</summary>
    [Fact]
    public void Food_mass_never_offers_cups()
    {
        var ids = UnitCatalog.ForFamily(UnitFamily.FoodMass).Select(u => u.Id).ToList();
        Assert.Equal(new[] { "g", "oz" }, ids);
    }

    [Fact]
    public void Water_does_offer_cups_because_its_density_is_the_safe_one()
        => Assert.True(UnitCatalog.IsKnown(UnitFamily.Volume, UnitCatalog.Cups));

    // ── Round-tripping: every pair, both directions ──────────────────────────

    /// <summary>
    /// Enter a value in a unit, store the canonical, read it back in the same unit: the
    /// owner must see the number they typed. Awkward values on purpose: a rat's weight,
    /// a fasting glucose, a cat's water bowl, a half-ounce of food.
    /// </summary>
    [Theory]
    // Weight (canonical kg)
    [InlineData(UnitFamily.Weight, "kg", "5.19")]
    [InlineData(UnitFamily.Weight, "kg", "0.35")]
    [InlineData(UnitFamily.Weight, "lb", "11.4")]
    [InlineData(UnitFamily.Weight, "lb", "0.9")]
    [InlineData(UnitFamily.Weight, "lb", "137.75")]
    [InlineData(UnitFamily.Weight, "g", "350")]
    [InlineData(UnitFamily.Weight, "g", "7")]
    // Glucose (canonical mmol/L)
    [InlineData(UnitFamily.Glucose, "mmol_l", "7.6")]
    [InlineData(UnitFamily.Glucose, "mmol_l", "22.1")]
    [InlineData(UnitFamily.Glucose, "mg_dl", "137")]
    [InlineData(UnitFamily.Glucose, "mg_dl", "43")]
    [InlineData(UnitFamily.Glucose, "mg_dl", "401")]
    // Volume (canonical ml)
    [InlineData(UnitFamily.Volume, "ml", "240")]
    [InlineData(UnitFamily.Volume, "fl_oz", "8.5")]
    [InlineData(UnitFamily.Volume, "fl_oz", "0.7")]
    [InlineData(UnitFamily.Volume, "cup", "1.25")]
    [InlineData(UnitFamily.Volume, "cup", "0.33")]
    // Food mass (canonical g)
    [InlineData(UnitFamily.FoodMass, "g", "85")]
    [InlineData(UnitFamily.FoodMass, "oz", "3.5")]
    [InlineData(UnitFamily.FoodMass, "oz", "0.5")]
    // Duration (canonical seconds)
    [InlineData(UnitFamily.Duration, "s", "45")]
    [InlineData(UnitFamily.Duration, "min", "1.5")]
    [InlineData(UnitFamily.Duration, "min", "12")]
    public void A_value_entered_and_read_back_in_the_same_unit_is_unchanged(
        UnitFamily family, string unitId, string entered)
    {
        var unit = UnitCatalog.Get(family, unitId);
        var value = decimal.Parse(entered, System.Globalization.CultureInfo.InvariantCulture);

        var canonical = UnitCatalog.ToCanonicalValue(value, unit);
        var readBack = UnitCatalog.Display(canonical, unit);

        Assert.Equal(value, readBack);
    }

    /// <summary>The same journey, one step further out: what the owner actually SEES.
    /// 11.4 lb must render "11.4", never "11.39" and never "11.40": the format drops
    /// trailing zeros, so a unit's <c>Decimals</c> is a ceiling rather than a padding
    /// width.</summary>
    [Theory]
    [InlineData(UnitFamily.Weight, "lb", "11.4", "11.4")]
    [InlineData(UnitFamily.Weight, "kg", "5.2", "5.2")]
    [InlineData(UnitFamily.Weight, "kg", "5.19", "5.19")]
    [InlineData(UnitFamily.Weight, "g", "350", "350")]
    [InlineData(UnitFamily.Glucose, "mg_dl", "137", "137")]
    [InlineData(UnitFamily.Glucose, "mmol_l", "7.6", "7.6")]
    [InlineData(UnitFamily.Duration, "min", "1.5", "1.5")]
    public void What_was_typed_is_what_is_rendered(
        UnitFamily family, string unitId, string entered, string expected)
    {
        var unit = UnitCatalog.Get(family, unitId);
        var value = decimal.Parse(entered, System.Globalization.CultureInfo.InvariantCulture);

        var canonical = UnitCatalog.ToCanonicalValue(value, unit);

        // Invariant culture so the assertion is about the NUMBER, not about whether the
        // test machine writes a comma. The app formats in the current culture.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(expected, UnitCatalog.Format(canonical, unit));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Every pair in every family, both directions, over a spread of values:
    /// convert out, convert back, and land on what you started with. This is the general
    /// form of the cases above and it is what makes adding a unit safe.</summary>
    [Fact]
    public void Every_pair_round_trips_in_both_directions()
    {
        decimal[] samples = { 0.5m, 1m, 3.25m, 7.6m, 45m, 137m, 350m, 1000.75m };

        foreach (UnitFamily family in Enum.GetValues<UnitFamily>())
        foreach (var from in UnitCatalog.ForFamily(family))
        foreach (var to in UnitCatalog.ForFamily(family))
        foreach (var sample in samples)
        {
            var canonical = UnitCatalog.ToCanonicalValue(sample, from);
            var inOther = UnitCatalog.FromCanonicalValue(canonical, to);
            var backToCanonical = UnitCatalog.ToCanonicalValue(inOther, to);
            var home = UnitCatalog.FromCanonicalValue(backToCanonical, from);

            // Not Assert.Equal on decimals: the intermediate divide can leave a
            // difference far below any unit's display precision, and pinning the exact
            // bits would be testing decimal's rounding rather than the conversion. What
            // must hold is that the drift can never reach the screen.
            Assert.True(Math.Abs(home - sample) < 0.0000000001m,
                $"{family}: {sample} {from.Id} → {to.Id} → {from.Id} came back as {home}.");
        }
    }

    /// <summary>The one number in this file that is a clinical fact rather than a
    /// convention. Pinned so a "simplification" to 18 can never happen quietly.</summary>
    [Fact]
    public void Glucose_converts_at_the_clinical_factor()
    {
        var mgdl = UnitCatalog.Get(UnitFamily.Glucose, UnitCatalog.MilligramsPerDecilitre);

        // 100 mg/dL is 5.55 mmol/L, the number on every conversion chart.
        var canonical = UnitCatalog.ToCanonicalValue(100m, mgdl);
        Assert.Equal(5.55m, decimal.Round(canonical, 2));

        // And back the other way: 10 mmol/L is 180 mg/dL.
        Assert.Equal(180m, UnitCatalog.Display(10m, mgdl));
        Assert.Equal(18.0182m, UnitCatalog.MgPerDlPerMmolPerL);
    }

    /// <summary>A rat weighs 300–500 g, which is "0.4 kg" or "0.9 lb": both useless to
    /// the person holding it. Grams for weight is why this repo ships import-rat.json.</summary>
    [Fact]
    public void A_rat_weighs_a_useful_number_of_grams()
    {
        var grams = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.GramsWeight);

        var canonical = UnitCatalog.ToCanonicalValue(412m, grams);
        Assert.Equal(0.412m, canonical);
        Assert.Equal(412m, UnitCatalog.Display(canonical, grams));
    }

    // ── The no-op edit ───────────────────────────────────────────────────────

    /// <summary>
    /// <b>An edit that does not touch the number must not rewrite it.</b> The owner opens
    /// an entry showing 11.4 lb, changes only the time, and saves. Re-parsing and
    /// re-converting runs the stored kilograms through a divide and then a multiply, and
    /// those two do not compose to the identity: the canonical value drifts a digit at a
    /// time, on every edit, in a medical record, with nothing anywhere reporting it.
    /// </summary>
    [Fact]
    public void An_untouched_save_writes_back_the_stored_value_byte_identical()
    {
        var lb = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Pounds);

        // What the sheet did when it opened: 5.17104 kg is exactly 11.4 lb.
        var stored = UnitCatalog.ToCanonicalValue(11.4m, lb);
        var shown = UnitCatalog.Format(stored, lb);

        // Ten opens and saves, changing nothing each time.
        var current = stored;
        for (var i = 0; i < 10; i++)
            current = UnitCatalog.CanonicalForSave(11.4m, lb, shown, current, shown, lb.Id);

        Assert.Equal(stored, current);
    }

    /// <summary>The guard is exact, not a tolerance: typing a different number saves the
    /// different number. A guard that swallowed small edits would be worse than no guard,
    /// because the value it discards is one the owner deliberately typed.</summary>
    [Fact]
    public void Changing_the_number_does_convert_it()
    {
        var lb = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Pounds);
        var stored = UnitCatalog.ToCanonicalValue(11.4m, lb);

        var saved = UnitCatalog.CanonicalForSave(11.6m, lb, "11.6", stored, "11.4", lb.Id);

        Assert.Equal(UnitCatalog.ToCanonicalValue(11.6m, lb), saved);
    }

    /// <summary>Changing only the UNIT re-derives too: 11.4 kg and 11.4 lb are the same
    /// text and different animals.</summary>
    [Fact]
    public void Changing_only_the_unit_reconverts_the_same_text()
    {
        var kg = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Kilograms);
        var lb = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Pounds);
        var stored = UnitCatalog.ToCanonicalValue(11.4m, kg);

        var saved = UnitCatalog.CanonicalForSave(11.4m, lb, "11.4", stored, "11.4", kg.Id);

        Assert.Equal(UnitCatalog.ToCanonicalValue(11.4m, lb), saved);
        Assert.NotEqual(stored, saved);
    }

    /// <summary>The comparison is on the TEXT, so "11.40" and "11.4" both leave the stored
    /// value alone. A parsed comparison would need a tolerance, and any tolerance is a
    /// threshold below which the app silently discards an edit the owner made.</summary>
    [Fact]
    public void Reformatted_text_is_still_a_change_and_is_converted_honestly()
    {
        var lb = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Pounds);
        var stored = UnitCatalog.ToCanonicalValue(11.4m, lb);

        var saved = UnitCatalog.CanonicalForSave(11.4m, lb, "11.40", stored, "11.4", lb.Id);

        // Re-derived rather than preserved, and it still lands on the same reading: the
        // conversion is exact, so the honest path costs nothing here.
        Assert.Equal(11.4m, UnitCatalog.Display(saved, lb));
    }

    // ── Stepper increments ───────────────────────────────────────────────────

    /// <summary>A gram stepper moves by a gram. It used to be a flat 0.1, which for a
    /// 412 g rat meant walking there in hundredths of a gram.</summary>
    [Theory]
    [InlineData(UnitFamily.Weight, "kg", "0.1")]
    [InlineData(UnitFamily.Weight, "lb", "0.1")]
    [InlineData(UnitFamily.Weight, "g", "1")]
    [InlineData(UnitFamily.Glucose, "mmol_l", "0.1")]
    [InlineData(UnitFamily.Glucose, "mg_dl", "1")]
    [InlineData(UnitFamily.Volume, "ml", "1")]
    [InlineData(UnitFamily.Duration, "s", "1")]
    public void A_stepper_moves_by_the_units_own_last_digit(
        UnitFamily family, string unitId, string expected)
    {
        var step = UnitCatalog.Step(UnitCatalog.Get(family, unitId));
        Assert.Equal(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), step);
    }

    // ── Legacy rows and unknown ids ──────────────────────────────────────────

    /// <summary>Every row written before units existed carries no unit, and it means
    /// the canonical one. This is the whole reason the change needs no backfill.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_row_with_no_unit_is_canonical(string? stored)
    {
        foreach (UnitFamily family in Enum.GetValues<UnitFamily>())
        {
            Assert.Equal(UnitCatalog.Canonical(family), UnitCatalog.Get(family, stored));
            Assert.False(UnitCatalog.IsKnown(family, stored));
        }
    }

    /// <summary>An id this build has never heard of (a newer client, a hand-edited
    /// cloud row) reads as canonical rather than throwing. The stored NUMBER is
    /// canonical either way, so the worst case is a label the owner can change, and the
    /// alternative is a pull that dies on one row and takes every other table with it.</summary>
    [Fact]
    public void An_unknown_unit_id_reads_as_canonical()
    {
        Assert.Equal(UnitCatalog.Canonical(UnitFamily.Weight),
            UnitCatalog.Get(UnitFamily.Weight, "stones"));
        Assert.False(UnitCatalog.IsKnown(UnitFamily.Weight, "stones"));
    }

    // ── Which record belongs to which family ─────────────────────────────────

    [Theory]
    [InlineData(TrackerId.Weight, UnitFamily.Weight)]
    [InlineData(TrackerId.Glucose, UnitFamily.Glucose)]
    [InlineData(TrackerId.Water, UnitFamily.Volume)]
    [InlineData(TrackerId.Appetite, UnitFamily.FoodMass)]
    [InlineData(TrackerId.Seizure, UnitFamily.Duration)]
    public void Each_measured_record_names_its_family(TrackerId tracker, UnitFamily expected)
        => Assert.Equal(expected, UnitCatalog.FamilyFor(tracker));

    /// <summary>Mood is not a number and must never acquire a unit: a stored 1–5 level
    /// is an observation, and AI/design-decisions.md forbids treating it as a
    /// measurement anywhere.</summary>
    [Fact]
    public void Mood_has_no_unit_family() => Assert.Null(UnitCatalog.FamilyFor(TrackerId.Mood));

    // ── The locale guess ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("US", "lb")]
    [InlineData("LR", "lb")]
    [InlineData("MM", "lb")]
    [InlineData("DE", "kg")]
    [InlineData("GB", "kg")]
    [InlineData(null, "kg")]
    [InlineData("", "kg")]
    public void Weight_guesses_metric_everywhere_but_three_countries(string? region, string expected)
        => Assert.Equal(expected, UnitCatalog.LocaleDefault(UnitFamily.Weight, region).Id);

    /// <summary>Glucose does NOT follow the imperial trio: mg/dL is a US clinical
    /// convention, and Liberia and Myanmar use mmol/L like everyone else.</summary>
    [Theory]
    [InlineData("US", "mg_dl")]
    [InlineData("LR", "mmol_l")]
    [InlineData("MM", "mmol_l")]
    [InlineData("DE", "mmol_l")]
    public void Glucose_guesses_mg_dl_only_in_the_united_states(string region, string expected)
        => Assert.Equal(expected, UnitCatalog.LocaleDefault(UnitFamily.Glucose, region).Id);

    [Fact]
    public void The_region_guess_is_case_insensitive()
        => Assert.Equal("lb", UnitCatalog.LocaleDefault(UnitFamily.Weight, "us").Id);

    /// <summary>Seconds everywhere: how long a seizure lasted is not a regional habit.</summary>
    [Theory]
    [InlineData("US")]
    [InlineData("DE")]
    public void Duration_is_seconds_everywhere(string region)
        => Assert.Equal("s", UnitCatalog.LocaleDefault(UnitFamily.Duration, region).Id);
}
