namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Import;
using Xunit;

/// <summary>
/// The import format's optional per-entry <c>unit</c>.
///
/// <para><b>Why the format grew a unit at all.</b> The guide's own rule is that the
/// format adapts to the model rather than the reverse, and the model now records which
/// unit each reading was written in. An owner's notes say "11.4 lb"; before this, the AI
/// had to convert that to kilograms itself, and every such conversion is a chance to
/// fabricate a reading in someone's medical record. Now it states the number and the
/// unit, and Felova does the arithmetic once, in the one place that is tested.</para>
///
/// <para><b>The asymmetry worth understanding.</b> A STORED row with an unrecognised
/// unit reads as canonical, because its number already is canonical whatever the label
/// says. An IMPORTED row with an unrecognised unit <b>rejects the file</b>, because its
/// number is only whatever the file claims. Quietly canonicalizing "11.4" that was meant
/// as pounds writes 11.4 kg into a diary, and nothing downstream can ever tell.</para>
/// </summary>
public class ImportUnitTests
{
    private static readonly DateTime Today = new(2026, 8, 22);

    private static ImportPlan Validate(string entriesJson)
    {
        var json = $$"""
            { "felova_import_version": 1, "pets": [
              { "match": "existing", "name": "Charly", "entries": [ {{entriesJson}} ] }
            ] }
            """;
        Assert.True(ImportFileParser.TryParse(json, out var file, out var error), error?.Message);
        return ImportValidator.Validate(file, Snapshot(), Today);
    }

    private static ImportSnapshot Snapshot() => new()
    {
        Pets = new[] { new ExistingPet(1, "Charly", "Dog") },
    };

    // ── Omitted means canonical ──────────────────────────────────────────────

    /// <summary>Every file written before this field existed still means exactly what it
    /// meant: each value field's own name already states the canonical unit.</summary>
    [Fact]
    public void A_weight_with_no_unit_is_kilograms()
    {
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 5.2 }""");

        Assert.True(plan.IsValid);
        var day = Assert.Single(plan.Pets[0].Days);
        Assert.Equal(5.2m, day.Weight);
        Assert.Equal(UnitCatalog.Kilograms, day.WeightUnit);
    }

    // ── A unit is honoured, and the stored value is canonical ────────────────

    /// <summary>The whole point: 11.4 lb is stored as 5.17104 kg and remembered as
    /// pounds, so the owner's diary reads back "11.4 lb" and every aggregate in the app
    /// keeps comparing one number.</summary>
    [Fact]
    public void A_weight_in_pounds_is_converted_and_its_unit_recorded()
    {
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 11.4, "unit": "lb" }""");

        Assert.True(plan.IsValid);
        var day = Assert.Single(plan.Pets[0].Days);
        Assert.Equal(UnitCatalog.Pounds, day.WeightUnit);

        var lb = UnitCatalog.Get(UnitFamily.Weight, UnitCatalog.Pounds);
        Assert.Equal(11.4m, UnitCatalog.Display(day.Weight!.Value, lb));
    }

    [Fact]
    public void A_glucose_reading_in_mg_dl_is_converted_and_its_unit_recorded()
    {
        var plan = Validate("""{ "type": "glucose", "date": "2026-08-10", "value": 137, "unit": "mg_dl", "context": "before_food" }""");

        Assert.True(plan.IsValid);
        var reading = Assert.Single(plan.Pets[0].Glucose);
        Assert.Equal(UnitCatalog.MilligramsPerDecilitre, reading.Unit);

        var mgdl = UnitCatalog.Get(UnitFamily.Glucose, UnitCatalog.MilligramsPerDecilitre);
        Assert.Equal(137m, UnitCatalog.Display(reading.Value, mgdl));
    }

    [Fact]
    public void A_water_amount_in_cups_is_converted_and_its_unit_recorded()
    {
        var plan = Validate("""{ "type": "water_amount", "date": "2026-08-10", "ml": 1, "unit": "cup" }""");

        Assert.True(plan.IsValid);
        var row = Assert.Single(plan.Pets[0].WaterAmounts);
        Assert.Equal(UnitCatalog.Cups, row.Unit);
        Assert.Equal(236.5882365m, row.AmountMl);
    }

    [Fact]
    public void A_food_amount_in_ounces_is_converted_and_its_unit_recorded()
    {
        var plan = Validate("""{ "type": "appetite_amount", "date": "2026-08-10", "grams": 3, "unit": "oz" }""");

        Assert.True(plan.IsValid);
        var row = Assert.Single(plan.Pets[0].AppetiteAmounts);
        Assert.Equal(UnitCatalog.Ounces, row.Unit);
        Assert.Equal(85.048569375m, row.Grams);
    }

    // ── Rejection, not silent canonicalization ───────────────────────────────

    [Fact]
    public void An_unknown_unit_rejects_the_file()
    {
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 11.4, "unit": "stones" }""");

        Assert.False(plan.IsValid);
        Assert.Contains("stones", plan.Errors[0].Message);
    }

    /// <summary>A unit from the wrong family is just as wrong as an invented one:
    /// "mg_dl" on a weight is a transcription that went astray, not a weight in mg/dL.</summary>
    [Fact]
    public void A_unit_from_another_family_rejects_the_file()
    {
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 11.4, "unit": "mg_dl" }""");

        Assert.False(plan.IsValid);
    }

    /// <summary>An entry with no convertible number may not name a unit. Ignoring the
    /// field would break the format's promise that everything not written is stated
    /// before the owner confirms.</summary>
    [Theory]
    [InlineData("""{ "type": "mood", "date": "2026-08-10", "level": 4, "unit": "kg" }""")]
    [InlineData("""{ "type": "water_level", "date": "2026-08-10", "level": 3, "unit": "ml" }""")]
    public void A_unit_on_an_entry_that_has_none_rejects_the_file(string entry)
    {
        var plan = Validate(entry);

        Assert.False(plan.IsValid);
        Assert.Contains("unit", plan.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Bounds are checked on the CANONICAL value ────────────────────────────

    /// <summary>
    /// The ceilings exist to catch a transcription slip ("18.4kg" typed as 1840) and they
    /// are stated in canonical units. Checking the raw number instead would reject a
    /// legitimate heavy animal weighed in pounds while waving through an absurd figure in
    /// cups.
    /// </summary>
    [Fact]
    public void A_plausible_weight_in_pounds_is_accepted_even_though_the_number_exceeds_the_kg_ceiling()
    {
        // 600 lb is 272 kg: a big dog-sized number for a pony, and well under the 500 kg
        // ceiling. The raw 600 would have tripped a ceiling stated in kilograms.
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 600, "unit": "lb" }""");

        Assert.True(plan.IsValid);
    }

    [Fact]
    public void An_implausible_weight_is_still_rejected_after_conversion()
    {
        // 2000 lb is 907 kg, past the ceiling: the decimal point slipped.
        var plan = Validate("""{ "type": "weight", "date": "2026-08-10", "value_kg": 2000, "unit": "lb" }""");

        Assert.False(plan.IsValid);
    }

    // ── Seizure duration ─────────────────────────────────────────────────────

    [Fact]
    public void A_seizure_duration_in_minutes_lands_on_whole_seconds()
    {
        var plan = Validate("""{ "type": "seizure", "date": "2026-08-10", "duration_seconds": 2, "unit": "min" }""");

        Assert.True(plan.IsValid);
        var seizure = Assert.Single(plan.Pets[0].Seizures);
        Assert.Equal(120, seizure.DurationSeconds);
        Assert.Equal(UnitCatalog.Minutes, seizure.Unit);
    }

    /// <summary>The legacy field still works, and is read as exactly what it always
    /// meant: seconds with a minutes unit, rather than as a second code path.</summary>
    [Fact]
    public void The_legacy_duration_minutes_field_still_imports()
    {
        var plan = Validate("""{ "type": "seizure", "date": "2026-08-10", "duration_minutes": 2 }""");

        Assert.True(plan.IsValid);
        var seizure = Assert.Single(plan.Pets[0].Seizures);
        Assert.Equal(120, seizure.DurationSeconds);
        Assert.Equal(UnitCatalog.Minutes, seizure.Unit);
    }

    /// <summary>Both fields at once is a transcription that went wrong. Picking a winner
    /// would import one of two contradictory claims about an animal.</summary>
    [Fact]
    public void Giving_both_duration_fields_rejects_the_file()
    {
        var plan = Validate("""
            { "type": "seizure", "date": "2026-08-10", "duration_seconds": 45, "duration_minutes": 2 }
            """);

        Assert.False(plan.IsValid);
    }

    /// <summary>The legacy field names its own unit, so it cannot carry another.</summary>
    [Fact]
    public void The_legacy_duration_field_may_not_carry_a_unit()
    {
        var plan = Validate("""
            { "type": "seizure", "date": "2026-08-10", "duration_minutes": 2, "unit": "s" }
            """);

        Assert.False(plan.IsValid);
    }
}
