namespace Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The Constellation's data shape.
//
//  One flattened, read-only projection of everything the owner has written down
//  for a pet over a stretch of time. It is a READ MODEL — nothing here is stored,
//  and nothing here may travel back into an entry store.
//
//  The rule the whole surface obeys, and the reason this type is as thin as it is:
//
//      Position is WHEN it happened. Symbol is WHAT happened. Density is HOW MUCH
//      was recorded. Nothing else is encoded.
//
//  So there is no severity, no score, no value axis and no ordering beyond time.
//  A glucose reading of 22 and a glucose reading of 4 are the same star in the same
//  place with the same size; the number lives in Detail, which is only ever read out
//  when the owner taps one. That is the same "Felova records; it never judges" rule
//  the weight trend and the vet report already follow (AI/design-decisions.md).
//
//  MAUI-free on purpose: this is compile-linked into the test project alongside
//  ConstellationLayout, so the geometry can be tested without a device.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// The eight kinds of thing that can appear in the sky. <b>Exactly eight, and the
/// count is load-bearing</b> — each one gets its own drawn symbol
/// (<c>CelestialSymbols</c>), and the symbols are distinguishable by SHAPE alone so
/// the sky is readable without relying on colour.
///
/// <para>The set is the app's six shipped trackers, plus medication doses (which are
/// not a tracker), plus one bucket for every tracker the owner defined themselves —
/// the same seven-plus-custom split <c>TodayCardCatalog</c> already uses. A ninth
/// member would need a ninth symbol that still reads as distinct at 5px, so adding
/// one is a design decision rather than a line.</para>
/// </summary>
public enum CelestialCategory
{
    Mood,
    Weight,
    Glucose,
    Appetite,
    Water,
    Seizure,
    Medication,
    Custom
}

/// <summary>
/// One thing that happened, at one moment. Built by <c>ConstellationService</c> from
/// the entry stores; consumed by the drawable (position + symbol) and by the detail
/// card the owner sees after a tap (<see cref="Title"/> + <see cref="Detail"/>).
/// </summary>
/// <param name="When">Local date+time. The ONLY value that affects placement.</param>
/// <param name="Category">Which symbol is drawn.</param>
/// <param name="Title">What it was, in the owner's language — "Weigh-in", or an
/// owner-defined tracker's own name (verbatim user text, never translated).</param>
/// <param name="Detail">The reading as it was written down ("4.8 mmol/L", "ate most
/// of it"), or empty. Shown only on tap — never rendered into the sky, because a value
/// on the canvas would be a second encoded dimension.</param>
public readonly record struct CelestialEvent(
    DateTime When,
    CelestialCategory Category,
    string Title,
    string Detail);

/// <summary>
/// How one category looks and reads — the Constellation's counterpart to
/// <c>TrackerVisuals</c>, and kept as one table for the same reason: the sky, the
/// legend and the detail card must never disagree about what a symbol means.
/// </summary>
/// <param name="LabelKey">AppStrings key for the category's name. A KEY, never a
/// resolved string — this is a static table and would otherwise survive a live
/// language switch in the old language (AI/coding-standards.md).</param>
/// <param name="ColorKey">Colour token from <c>Resources/Styles/Colors.xaml</c>,
/// resolved through <c>Helpers/AppColors</c>. The Star* tokens are the night-sky
/// siblings of the daylight tracker accents — a weigh-in is blue in both.</param>
public readonly record struct CelestialVisual(string LabelKey, string ColorKey);

/// <summary>The one category → (label, colour) table.</summary>
public static class CelestialVisuals
{
    private static readonly CelestialVisual[] Map =
    {
        new("Journal_MoodTitle", "StarMood"),
        new("Journal_WeighIn", "StarWeight"),
        new("Journal_GlucoseCheck", "StarGlucose"),
        new("Journal_Appetite", "StarAppetite"),
        new("Journal_Water", "StarWater"),
        new("Journal_Seizure", "StarSeizure"),
        new("Sky_Medication", "StarMedication"),
        new("Sky_YourOwn", "StarCustom"),
    };

    /// <summary>Every category, in symbol-legend order.</summary>
    public static IReadOnlyList<CelestialCategory> All { get; } = new[]
    {
        CelestialCategory.Mood,
        CelestialCategory.Weight,
        CelestialCategory.Glucose,
        CelestialCategory.Appetite,
        CelestialCategory.Water,
        CelestialCategory.Seizure,
        CelestialCategory.Medication,
        CelestialCategory.Custom,
    };

    /// <summary>Index-safe: an out-of-range value (a row written by a newer build)
    /// falls back to the owner-defined bucket rather than throwing, the same posture
    /// as <c>TrackerVisuals.Fallback</c>.</summary>
    public static CelestialVisual For(CelestialCategory category)
    {
        var i = (int)category;
        return i >= 0 && i < Map.Length ? Map[i] : Map[^1];
    }

    /// <summary>
    /// The kind to open focused on, given the pet's conditions — or null when nothing
    /// suggests one.
    ///
    /// <para>It exists because <b>volume is not importance</b>. Twice-daily medication
    /// is a hundred and eighty entries in a quarter and six seizures are six, so a sky
    /// showing everything equally shows mostly doses. Sizing seizures larger would be
    /// the app ranking them; opening with the kind the owner most likely came for,
    /// which they can change with one tap, is not.</para>
    ///
    /// <para>Same shape and the same condition ids as <c>TodayCardCatalog.DefaultsFor</c>:
    /// a derived default, nothing written until the owner expresses an intent of their
    /// own. Nothing is hidden either — the rest of the sky is dimmed, not removed.</para>
    /// </summary>
    public static CelestialCategory? OpeningFocusFor(IEnumerable<string?>? conditionIds)
    {
        foreach (var id in conditionIds ?? Array.Empty<string?>())
        {
            switch (id)
            {
                case "epilepsy": return CelestialCategory.Seizure;
                case "diabetes": return CelestialCategory.Glucose;
                case "ckd": return CelestialCategory.Water;
            }
        }

        return null;
    }

    /// <summary>The category's localized name, resolved now (never cached).</summary>
    public static string Label(CelestialCategory category) =>
        Helpers.LocalizationManager.Instance.GetString(For(category).LabelKey);
}
