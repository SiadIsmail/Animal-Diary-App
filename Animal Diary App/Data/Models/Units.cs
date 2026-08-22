namespace Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Units: the one place a unit is declared.
//
//  Three rules the owner set, and every line below follows from one of them:
//
//   1. The unit is chosen PER ENTRY, on the input sheet.
//   2. The last choice is remembered and prefills the next one.
//   3. Display follows the MAJORITY of the owner's own entries for that record,
//      and minority entries are converted into it. No settings screen, no
//      migration prompt, no "choose your units" step.
//
//  Rule 3 is why the display unit is DERIVED, never stored: the same shape as
//  Pet.AgeYears, TodayCardCatalog.DefaultsFor and VetVisit's past-ness. The
//  resolution lives in Data/Services/Journal/DisplayUnitResolver.cs.
//
//  THE STORED VALUE STAYS CANONICAL. The unit column on an entry records what the
//  owner typed in; it is PROVENANCE, not the value. That is load-bearing: every
//  aggregate in the app (RecordFactsBuilder's min/max, the report's daily sums, the
//  charts) operates on one comparable number and keeps working untouched. Storing
//  raw entered values instead would break every one of them, silently.
//
//  Ids are STABLE and NEVER LOCALIZED: they are stored data, and stored data is
//  never translated (AI/coding-standards.md). The label is a resource key resolved
//  per read, so a live language switch relabels an open screen like everything else.
//
//  WHAT IS DELIBERATELY NOT HERE:
//   • Medication dose units (mg, IU, mL, tablets, drops, puffs). It looks like the
//     same problem and it is not: 2 IU of insulin cannot be converted into mg, the
//     ratio is drug-specific and clinically meaningless. Medication.Unit stays free
//     text, chosen once, never converted, never majority-resolved.
//   • Observation levels (mood, appetite level, water level). Those are words, not
//     numbers, and AI/design-decisions.md forbids treating them as numbers at all.
//   • Cups of food. Owners do measure dry food in cups and it is tempting to add to
//     FoodMass. A cup is a VOLUME and grams is a MASS, so the conversion depends on
//     the food's density and any factor picked here is wrong for most foods. Volume
//     cups for WATER are fine: water's density is the one that is safely constant.
//   • Temperature. Not a tracker yet. The mechanism is general enough that °C/°F
//     costs one row here plus a family when it becomes one.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A set of units that convert into one another. The canonical member of a family is
/// the unit every stored value is held in.
/// </summary>
public enum UnitFamily
{
    /// <summary>Body weight. Canonical kilograms.</summary>
    Weight,

    /// <summary>Blood glucose. Canonical mmol/L.</summary>
    Glucose,

    /// <summary>Water taken in. Canonical millilitres.</summary>
    Volume,

    /// <summary>Food eaten, by mass. Canonical grams.</summary>
    FoodMass,

    /// <summary>How long something lasted. Canonical seconds.</summary>
    Duration
}

/// <summary>
/// One unit: its stable stored id, how it reaches the canonical value, and how it is
/// written down.
/// </summary>
/// <param name="Id">The stable string that gets STORED on an entry ("lb", "mg_dl").
/// Unique within its family, never localized, never reused for a different meaning.
/// Renaming one orphans every row that carries it, locally and in the cloud.</param>
/// <param name="Family">Which set it converts within.</param>
/// <param name="LabelKey">AppStrings key for the display label ("lb", "mg/dL"),
/// resolved per read so a live language switch reaches an open screen.</param>
/// <param name="ToCanonical">Multiply a value in this unit by this to reach the
/// canonical value. Exactly 1 for the canonical unit itself.</param>
/// <param name="Decimals">Display precision for this unit. Trailing zeros are dropped
/// (see <see cref="UnitCatalog.Format"/>), so this is a ceiling rather than a padding
/// width: entering 11.4 lb and reading it back must render "11.4", not "11.40".</param>
/// <param name="IsCanonical">Whether stored values are held in this unit.</param>
public readonly record struct UnitDef(
    string Id,
    UnitFamily Family,
    string LabelKey,
    decimal ToCanonical,
    int Decimals,
    bool IsCanonical)
{
    /// <summary>The localized label ("lb", "mg/dL"), resolved now and cached nowhere.</summary>
    public string Label => Helpers.LocalizationManager.Instance.GetString(LabelKey);
}

/// <summary>
/// Every unit the app offers, in the same shape as <see cref="ConditionCatalog"/> and
/// <see cref="TodayCardCatalog"/>: one static table, read everywhere, written nowhere.
///
/// <para><b>Lookup is family-scoped, deliberately.</b> "g" is a weight unit (a rat
/// weighs 350 g) and a food-mass unit, and the two carry different factors because
/// their families have different canonicals (kg and g). Every caller knows which
/// record it is reading (the column belongs to one entry store, whose family is
/// fixed), so <see cref="Get"/> takes the family and can never resolve an id to the
/// wrong factor. A bare <c>Get(id)</c> would compile at every call site and be wrong
/// at exactly one of them.</para>
/// </summary>
public static class UnitCatalog
{
    // ── Stable ids. Constants because they are STORED, so a typo at a write site and
    //    a typo at a read site must not be able to disagree silently. ──
    public const string Kilograms = "kg";
    public const string Pounds = "lb";
    public const string GramsWeight = "g";

    public const string MmolPerLitre = "mmol_l";
    public const string MilligramsPerDecilitre = "mg_dl";

    public const string Millilitres = "ml";
    public const string FluidOunces = "fl_oz";
    public const string Cups = "cup";

    public const string GramsFood = "g";
    public const string Ounces = "oz";

    public const string Seconds = "s";
    public const string Minutes = "min";

    /// <summary>
    /// mmol/L → mg/dL is ×18.0182, so the reverse factor is a repeating decimal. It is
    /// computed once here at full <see cref="decimal"/> width rather than written out
    /// as a truncated literal: a short literal is a rounding error baked into every
    /// American diabetic's readings.
    ///
    /// <para>This is the highest-stakes pair in the app. The two units differ by a
    /// factor of eighteen, so a value shown in the wrong one is not slightly wrong,
    /// it is a different clinical picture.</para>
    /// </summary>
    public const decimal MgPerDlPerMmolPerL = 18.0182m;

    private static readonly decimal MgDlToMmol = 1m / MgPerDlPerMmolPerL;

    /// <summary>Every unit, grouped by family, canonical first within each.</summary>
    public static IReadOnlyList<UnitDef> All { get; } = new List<UnitDef>
    {
        // ── Weight (canonical kg) ────────────────────────────────────────────
        // Grams is not padding. This repo ships import-rat.json, and a rat weighs
        // 300–500 g: "0.4 kg" and "0.9 lb" are both useless to the person holding it.
        new(Kilograms,   UnitFamily.Weight, "Unit_Kg", 1m,            2, true),
        new(Pounds,      UnitFamily.Weight, "Unit_Lb", 0.45359237m,   2, false),
        new(GramsWeight, UnitFamily.Weight, "Unit_G",  0.001m,        0, false),

        // ── Glucose (canonical mmol/L) ───────────────────────────────────────
        // Without mg/dL the app is effectively unusable for a diabetic pet in the US.
        new(MmolPerLitre,            UnitFamily.Glucose, "Unit_MmolL", 1m,          1, true),
        new(MilligramsPerDecilitre,  UnitFamily.Glucose, "Unit_MgDl",  MgDlToMmol,  0, false),

        // ── Volume (canonical mL) ────────────────────────────────────────────
        // Cups are safe HERE and only here: water's density is the constant one.
        new(Millilitres, UnitFamily.Volume, "Unit_Ml",    1m,               0, true),
        new(FluidOunces, UnitFamily.Volume, "Unit_FlOz",  29.5735295625m,   1, false),
        new(Cups,        UnitFamily.Volume, "Unit_Cup",   236.5882365m,     2, false),

        // ── Food mass (canonical g) ──────────────────────────────────────────
        // g and oz ONLY. A cup of dry food is a volume and this is a mass; the
        // conversion depends on the food's density, so any factor picked here is
        // wrong for most foods. Do not add one, however often it is asked for.
        new(GramsFood, UnitFamily.FoodMass, "Unit_G",  1m,              0, true),
        new(Ounces,    UnitFamily.FoodMass, "Unit_Oz", 28.349523125m,   1, false),

        // ── Duration (canonical seconds) ─────────────────────────────────────
        // Seconds is canonical because owners describe seizures in seconds and
        // sub-minute events are common and clinically relevant.
        new(Seconds, UnitFamily.Duration, "Unit_Sec", 1m,   0, true),
        new(Minutes, UnitFamily.Duration, "Unit_Min", 60m,  1, false),
    };

    /// <summary>The units one family offers, canonical first: what a picker lists.</summary>
    public static IReadOnlyList<UnitDef> ForFamily(UnitFamily family)
    {
        var units = new List<UnitDef>();
        foreach (var unit in All)
            if (unit.Family == family)
                units.Add(unit);
        return units;
    }

    /// <summary>
    /// The unit an entry's stored id names, or the family's canonical when the id is
    /// null, empty or unrecognised.
    ///
    /// <para><b>NULL MEANS CANONICAL</b>, which is true of every row written before
    /// units existed: that is what makes this change need no backfill. An unrecognised
    /// id (a newer client's unit, a hand-edited row) also reads as canonical rather
    /// than throwing: the stored NUMBER is canonical either way, so the worst case is
    /// a label the owner can change, not a wrong reading.</para>
    /// </summary>
    public static UnitDef Get(UnitFamily family, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
            foreach (var unit in All)
                if (unit.Family == family && unit.Id == id)
                    return unit;

        return Canonical(family);
    }

    /// <summary>Whether a stored id names a real unit of this family. Used by the
    /// import validator, which must reject a file rather than quietly canonicalize it:
    /// an import that silently reinterprets 11.4 lb as 11.4 kg is a fabricated
    /// reading.</summary>
    public static bool IsKnown(UnitFamily family, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        foreach (var unit in All)
            if (unit.Family == family && unit.Id == id)
                return true;
        return false;
    }

    /// <summary>The unit a family's values are stored in.</summary>
    public static UnitDef Canonical(UnitFamily family)
    {
        foreach (var unit in All)
            if (unit.Family == family && unit.IsCanonical)
                return unit;

        // Unreachable: every family above declares exactly one canonical, and
        // CanonicalIsUniquePerFamily in the tests is what keeps that true.
        throw new InvalidOperationException($"No canonical unit for {family}.");
    }

    // ── Conversion ───────────────────────────────────────────────────────────
    //
    //  NEVER ROUND ON WRITE. 11.4 lb is 5.17104 kg, not 5.17. Rounding happens once,
    //  at display, using the display unit's own Decimals. Anything else silently
    //  corrupts a medical number a little more on every edit.

    /// <summary>The canonical value for something the owner entered in
    /// <paramref name="unit"/>. Full precision: no rounding here, ever.</summary>
    public static decimal ToCanonicalValue(decimal entered, UnitDef unit) =>
        unit.IsCanonical ? entered : entered * unit.ToCanonical;

    /// <summary>The same reading expressed in <paramref name="unit"/>, unrounded.
    /// Round only when you are about to write it on the screen.</summary>
    public static decimal FromCanonicalValue(decimal canonical, UnitDef unit) =>
        unit.IsCanonical ? canonical : canonical / unit.ToCanonical;

    /// <summary>The canonical reading rounded to what <paramref name="unit"/> shows.
    /// The one rounding point in the whole mechanism.</summary>
    public static decimal Display(decimal canonical, UnitDef unit) =>
        decimal.Round(FromCanonicalValue(canonical, unit), unit.Decimals, MidpointRounding.AwayFromZero);

    /// <summary>The .NET format string for a unit's precision, trailing zeros dropped
    /// (<c>"0.##"</c> for two): the same shape the app's single weight
    /// formatter has always used. Public so a surface that must pass a format string (a MigraDoc cell, a
    /// XAML StringFormat) uses this rather than a copy of it.</summary>
    public static string FormatString(UnitDef unit) =>
        unit.Decimals <= 0 ? "0" : "0." + new string('#', unit.Decimals);

    /// <summary>The number alone, in the display unit, in the current culture: a
    /// German reader sees <c>5,19</c>. The unit LABEL is added by the caller (see
    /// <c>Helpers/UnitText</c>), because some surfaces set the two in different
    /// sizes.</summary>
    public static string Format(decimal canonical, UnitDef unit) =>
        Display(canonical, unit).ToString(
            FormatString(unit), System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// The same band expressed in another unit, unrounded.
    ///
    /// <para><b>This is the most dangerous conversion in the app.</b> A target band is
    /// stored in the unit it was ENTERED in (<c>Tracker.Unit</c>), while readings are
    /// stored canonical, so the two can disagree by design. If a display resolves to
    /// mg/dL and the band is left as the stored "4-8", an owner sees three-digit readings
    /// against a two-digit band: not merely wrong, but wrong in the direction that reads
    /// as catastrophically low blood sugar. Every surface that puts a band beside a
    /// reading goes through here.</para>
    ///
    /// <para>Converted through the canonical value rather than by a combined factor, so
    /// it composes exactly the way every other conversion in this file does.</para>
    /// </summary>
    public static TargetRange ConvertRange(TargetRange range, UnitDef from, UnitDef to) =>
        new(FromCanonicalValue(ToCanonicalValue(range.Lo, from), to),
            FromCanonicalValue(ToCanonicalValue(range.Hi, from), to));

    /// <summary>
    /// A band in canonical units, ready to be compared against stored readings.
    ///
    /// <para>Comparing a reading to a raw stored band was already a latent bug before
    /// mg/dL existed: it happened to be right only because every band in the world was
    /// mmol/L. A comparison happens in canonical space or it does not happen.</para>
    /// </summary>
    public static TargetRange CanonicalRange(TargetRange range, UnitDef storedIn) =>
        ConvertRange(range, storedIn, Canonical(storedIn.Family));

    /// <summary>
    /// What an input sheet should STORE when the owner presses Save: the value they
    /// opened with, untouched, or a fresh conversion of what they typed.
    ///
    /// <para><b>An edit that does not touch the number must not rewrite it.</b> If the
    /// owner opens an entry showing 11.4 lb, changes only the time, and saves, re-parsing
    /// and re-converting runs the stored kilograms through a divide and then a multiply.
    /// Those two do not compose to the identity, so the canonical value drifts a digit at
    /// a time, on every edit, silently, in a medical record. The guard is exact: the value
    /// is re-derived only when the typed TEXT or the chosen UNIT actually changed.</para>
    ///
    /// <para>Comparing the text rather than the parsed number is deliberate. "11.40" and
    /// "11.4" parse the same and both mean the stored value should stand; a parsed
    /// comparison would also have to decide what tolerance counts as "the same", and any
    /// answer to that is a threshold below which the app quietly discards an edit.</para>
    ///
    /// <para>It lives here, and it is pure, because it is the one rule in the whole
    /// mechanism whose failure is invisible: the app keeps working, the numbers just stop
    /// being the ones the owner wrote down.</para>
    /// </summary>
    /// <param name="typed">The number currently in the field, parsed.</param>
    /// <param name="unit">The unit currently selected.</param>
    /// <param name="typedText">The field's text exactly as it now reads.</param>
    /// <param name="openedCanonical">The canonical value already on the row when the
    /// sheet opened. Ignored for a new entry (pass anything; nothing was opened).</param>
    /// <param name="openedText">The field's text when the sheet opened.</param>
    /// <param name="openedUnitId">The unit id the sheet opened on.</param>
    public static decimal CanonicalForSave(
        decimal typed, UnitDef unit, string typedText,
        decimal openedCanonical, string openedText, string openedUnitId)
    {
        var untouched =
            string.Equals(typedText, openedText, StringComparison.Ordinal)
            && string.Equals(unit.Id, openedUnitId, StringComparison.Ordinal);

        return untouched ? openedCanonical : ToCanonicalValue(typed, unit);
    }

    /// <summary>
    /// How much one tap of a stepper moves a value in this unit: one step of the
    /// unit's own last displayed digit, and 1 for a whole-number unit.
    ///
    /// <para>Derived from <see cref="UnitDef.Decimals"/> rather than listed per unit,
    /// because the two answer the same question and a table of both is a table that can
    /// disagree with itself. It is what stops a gram stepper from moving 0.1 g at a time
    /// (a rat would need eight hundred taps) while keeping kilograms at 0.1.</para>
    /// </summary>
    public static decimal Step(UnitDef unit) => unit.Decimals switch
    {
        <= 0 => 1m,
        // A weight in kg or lb steps by 0.1, not by 0.01: the second decimal exists so
        // an owner can TYPE 5.19, not so a stepper has to walk there.
        _ => 0.1m,
    };

    // ── Which family a record belongs to ─────────────────────────────────────

    /// <summary>
    /// The family a shipped tracker's readings are measured in, or null for a record
    /// that carries no convertible number.
    ///
    /// <para>Mood has no number at all. An owner-defined tracker has a number but its
    /// unit is free text the owner typed ("km", "poops") and nothing can convert it,
    /// so it is deliberately absent here too: <c>CustomEntry.Unit</c> is stored and
    /// displayed verbatim, never resolved through this catalog.</para>
    /// </summary>
    public static UnitFamily? FamilyFor(TrackerId tracker) => tracker switch
    {
        TrackerId.Weight => UnitFamily.Weight,
        TrackerId.Glucose => UnitFamily.Glucose,
        TrackerId.Water => UnitFamily.Volume,
        TrackerId.Appetite => UnitFamily.FoodMass,
        TrackerId.Seizure => UnitFamily.Duration,
        _ => null,
    };

    // ── The locale guess (last resort only) ──────────────────────────────────

    /// <summary>
    /// The unit to prefill a picker with for someone who has never logged this record
    /// and has no remembered choice. Metric everywhere except the United States,
    /// Liberia and Myanmar; glucose is mg/dL in the US and mmol/L everywhere else
    /// (including Liberia and Myanmar, which use mmol/L clinically).
    ///
    /// <para>It is a guess, but an INVISIBLE one: it only prefills a picker the owner
    /// can change, and the moment they log anything the majority rule takes over. No
    /// stored value ever depends on it.</para>
    /// </summary>
    /// <param name="regionCode">Two-letter ISO region ("US", "DE"), case-insensitive.
    /// Null or unrecognised is treated as metric.</param>
    public static UnitDef LocaleDefault(UnitFamily family, string? regionCode)
    {
        var region = regionCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var imperial = region is "US" or "LR" or "MM";

        return family switch
        {
            UnitFamily.Weight => Get(family, imperial ? Pounds : Kilograms),
            UnitFamily.Volume => Get(family, imperial ? FluidOunces : Millilitres),
            UnitFamily.FoodMass => Get(family, imperial ? Ounces : GramsFood),
            // Only the US uses mg/dL; the imperial trio is not the right set here.
            UnitFamily.Glucose => Get(family, region == "US" ? MilligramsPerDecilitre : MmolPerLitre),
            // Seconds everywhere: it is how a seizure is described, not a regional habit.
            _ => Canonical(family),
        };
    }
}
