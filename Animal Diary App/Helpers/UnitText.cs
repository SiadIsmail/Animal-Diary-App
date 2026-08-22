namespace Animal_Diary_App.Helpers;

using Animal_Diary_App.Data.Models;

/// <summary>
/// How a reading reads. <b>One formatter, every surface</b>: Today's meta line, the
/// stat cards, the Journal timeline, the Constellation, the facts panel and the vet
/// report.
///
/// <para><b>Why this exists.</b> Today's meta line rendered <c>5.19 kg</c> while the
/// stat card beside it rendered <c>5.2 kg</c>: the same weigh-in, two numbers, one
/// screen. In an app people trust with medical numbers that is a trust bug wearing a
/// cosmetic disguise: a reader who notices it has to work out which of the two is the
/// number they typed, and the answer was "neither, exactly".</para>
///
/// <para>It replaced <c>WeightText</c>, which was the same idea for one record. Units
/// are now chosen per entry, so every measured record has the identical problem and
/// deserves the identical answer: a second formatter for glucose would recreate the
/// bug one family over.</para>
///
/// <para><b>The value handed in is always CANONICAL</b> (kg, mmol/L, mL, g, seconds);
/// the conversion into the owner's unit happens here and nowhere else, which is what
/// makes "one conversion point" true rather than aspirational. Precision and trailing
/// zeros come from the unit's own <see cref="UnitDef.Decimals"/>, so 11.4 lb reads
/// "11.4 lb" and never "11.40 lb".</para>
///
/// <para>Current culture, so a German reader sees <c>5,19</c>. Nothing is cached: a
/// live language switch has to reach an open page.</para>
/// </summary>
public static class UnitText
{
    /// <summary>The number alone, converted into <paramref name="unit"/>: <c>"11.4"</c>.
    /// Exposed separately because some surfaces set the number and the unit in two
    /// different sizes (Today's stat card).</summary>
    public static string Number(decimal canonical, UnitDef unit) =>
        UnitCatalog.Format(canonical, unit);

    /// <summary>The unit's own label, with no leading space: <c>"lb"</c>. Resolved per
    /// call so a live language switch relabels an open screen.</summary>
    public static string Label(UnitDef unit) => unit.Label;

    /// <summary>The number with its unit: <c>"11.4 lb"</c>. <b>The default everywhere.</b>
    /// With units variable a bare number is genuinely ambiguous, and 14 mmol/L against
    /// 14 mg/dL is not a rounding difference, it is a different clinical picture.</summary>
    public static string WithUnit(decimal canonical, UnitDef unit) =>
        Number(canonical, unit) + " " + Label(unit);

    /// <summary>
    /// A target band, converted out of the unit it was ENTERED in and into the unit the
    /// readings beside it are shown in: <c>"Target 72-144 mg/dL"</c>.
    ///
    /// <para><b>Every surface that shows a band calls this, and nothing else formats
    /// one.</b> A band is stored in <c>Tracker.Unit</c> while readings are stored
    /// canonical, so the two genuinely can disagree, and the failure is not a cosmetic
    /// one: mg/dL readings against a band still reading "4-8" is a screen that says the
    /// animal is in a hypoglycaemic emergency. Passing both units is deliberate, so a
    /// call site cannot silently print the stored numbers.</para>
    ///
    /// <para>The band is <b>stated, never applied</b>. Nothing here colours, flags or
    /// scores a reading against it: Felova records and does not judge
    /// (AI/design-decisions.md). This is only about the band being written in a unit that
    /// matches the numbers next to it.</para>
    /// </summary>
    /// <param name="range">The band as stored.</param>
    /// <param name="storedIn">The unit it was entered in (<c>Tracker.Unit</c>).</param>
    /// <param name="showIn">The unit the readings beside it are displayed in.</param>
    public static string Band(TargetRange range, UnitDef storedIn, UnitDef showIn)
    {
        var shown = UnitCatalog.ConvertRange(range, storedIn, showIn);
        return LocalizationManager.Instance.Format(
            "Common_TargetBand",
            UnitCatalog.Format(shown.Lo, showIn),
            UnitCatalog.Format(shown.Hi, showIn),
            Label(showIn));
    }

    /// <summary>A signed change, in the display unit: <c>"+0.4 lb"</c> / <c>"−0.2 lb"</c>.
    ///
    /// <para>The sign is the whole statement and it is neutral: a weight change is a
    /// fact, never coloured good or bad (AI/design-decisions.md, "Felova records; it
    /// never judges"). A difference is converted as a difference, never by converting
    /// the two endpoints and subtracting the rounded results.</para>
    /// </summary>
    public static string Change(decimal canonicalDelta, UnitDef unit)
    {
        var shown = UnitCatalog.Display(canonicalDelta, unit);

        // The minus sign, not a hyphen: it is a mathematical sign and the app already
        // uses the real glyph in the weight stepper.
        var sign = shown < 0 ? "−" : "+";
        return sign + Number(Math.Abs(canonicalDelta), unit) + " " + Label(unit);
    }
}
