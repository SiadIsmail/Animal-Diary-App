namespace Animal_Diary_App.Data.Services.Import;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The import format's VOCABULARY — the one place the wire strings live.
//
//  AI/import-guide.md is written from this file. If a value here changes, the guide
//  is wrong until it is updated in the same change: the guide is the contract another
//  AI generates against, and a drifted contract produces files that validate as
//  garbage rather than failing loudly.
//
//  Wire strings are snake_case and deliberately NOT the C# enum member names. Those
//  enums are storage formats (SeizureType's member names are the cloud wire format;
//  renaming one orphans every synced row — see JournalEntries.cs), so pinning the
//  import vocabulary separately means an import file can never become the reason a
//  storage enum cannot be renamed.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which entry an import row describes. Each maps onto a store the app
/// already has — this enum introduces no new kind of data.</summary>
public enum ImportEntryType
{
    Weight,
    Mood,
    Glucose,
    AppetiteLevel,
    AppetiteAmount,
    WaterLevel,
    WaterAmount,
    Seizure,

    /// <summary>An occurrence of a tracker the owner defined (<see cref="CustomTracker"/>),
    /// named by a file-local ref rather than a row id.</summary>
    Custom
}

/// <summary>Whether a pet block appends to a pet already on the device or creates one.
/// Stated explicitly in the file and never inferred — see <see cref="ImportValidator"/>.</summary>
public enum ImportPetMatch
{
    Existing,
    New
}

public static class ImportFormat
{
    /// <summary>The only format version this build accepts. A file carrying anything
    /// else is rejected outright rather than parsed optimistically: a newer file may
    /// use fields whose ABSENCE this build would silently read as "not recorded",
    /// which is indistinguishable from data loss.</summary>
    public const int Version = 1;

    /// <summary>The top-level key carrying <see cref="Version"/>. Named in errors, so
    /// a file missing it gets told exactly what to add.</summary>
    public const string VersionKey = "felova_import_version";

    /// <summary>Weight is kilograms everywhere in this app (the vet report labels its
    /// series "kg"), so the format names the unit in the field itself — value_kg —
    /// rather than accepting a unit the importer would have to convert. A conversion is
    /// a place to be silently wrong about a medical number.</summary>
    public const string WeightUnit = "kg";

    // ── Entry types ─────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, ImportEntryType> EntryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weight"] = ImportEntryType.Weight,
        ["mood"] = ImportEntryType.Mood,
        ["glucose"] = ImportEntryType.Glucose,
        ["appetite_level"] = ImportEntryType.AppetiteLevel,
        ["appetite_amount"] = ImportEntryType.AppetiteAmount,
        ["water_level"] = ImportEntryType.WaterLevel,
        ["water_amount"] = ImportEntryType.WaterAmount,
        ["seizure"] = ImportEntryType.Seizure,
        ["custom"] = ImportEntryType.Custom,
    };

    /// <summary>The wire name for a type — used in errors and in the preview, so the
    /// owner reads the same word they would find in the file.</summary>
    public static string NameOf(ImportEntryType type) =>
        EntryTypes.First(kv => kv.Value == type).Key;

    public static bool TryParseEntryType(string? value, out ImportEntryType type) =>
        EntryTypes.TryGetValue((value ?? string.Empty).Trim(), out type);

    /// <summary>Every accepted entry type, for the "unknown type" error message. An
    /// unknown type is a hard error rather than a skipped row: it is far more likely to
    /// be a generating AI's invention than a field from the future, and silently
    /// dropping it would lose data the owner believes they imported.</summary>
    public static IReadOnlyList<string> EntryTypeNames { get; } = EntryTypes.Keys.OrderBy(k => k).ToList();

    // ── Pet match ───────────────────────────────────────────────────────────────

    public static bool TryParsePetMatch(string? value, out ImportPetMatch match)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "existing": match = ImportPetMatch.Existing; return true;
            case "new": match = ImportPetMatch.New; return true;
            default: match = default; return false;
        }
    }

    // ── Value vocabularies ──────────────────────────────────────────────────────

    public static bool TryParseFoodContext(string? value, out FoodContext context)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "before_food": context = FoodContext.BeforeFood; return true;
            case "after_food": context = FoodContext.AfterFood; return true;
            default: context = default; return false;
        }
    }

    public static bool TryParseSeizureType(string? value, out SeizureType type)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "generalized": type = SeizureType.Generalized; return true;
            case "focal": type = SeizureType.Focal; return true;
            case "focal_to_generalized": type = SeizureType.FocalToGeneralized; return true;
            default: type = default; return false;
        }
    }

    public static bool TryParseShape(string? value, out CustomShape shape)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "tick": shape = CustomShape.Tick; return true;
            case "amount": shape = CustomShape.Amount; return true;
            default: shape = default; return false;
        }
    }

    /// <summary>The cadence vocabulary, shared with the shipped trackers
    /// (<see cref="TrackerKind"/>). Optional in a file — see
    /// <see cref="DefaultCustomCadence"/>.</summary>
    public static bool TryParseCadence(string? value, out TrackerKind kind)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "per_day": kind = TrackerKind.PerDay; return true;
            case "daily": kind = TrackerKind.Daily; return true;
            case "weekly": kind = TrackerKind.Weekly; return true;
            case "twice_weekly": kind = TrackerKind.TwiceWeekly; return true;
            case "as_needed": kind = TrackerKind.AsNeeded; return true;
            case "event": kind = TrackerKind.Event; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>
    /// What an imported custom tracker's cadence is when the file doesn't say.
    ///
    /// <para><see cref="TrackerKind.Event"/> because a cadence is how often the app
    /// ASKS, which is an owner's preference and not a fact anywhere in a pile of notes.
    /// Event never appears in "Still to do", so a guess can never put "sick yet?" on a
    /// to-do list — and the owner changes it in one tap on the Manage page if they do
    /// want to be asked.</para>
    /// </summary>
    public const TrackerKind DefaultCustomCadence = TrackerKind.Event;

    // ── Bounds ──────────────────────────────────────────────────────────────────
    //
    // These mirror what the app's own input sheets accept, so an imported row can never
    // be a value the owner could not have typed. Where a sheet has no bound (glucose
    // and weight are only clamped to >= 0), the import applies a loose sanity ceiling
    // instead — an AI transcribing "18.4kg" as 1840 is the failure this catches, and a
    // ceiling no real animal reaches costs nothing.

    /// <summary>Levels are 1..5 everywhere (mood, appetite, water). 0 is "None", which
    /// is the app's way of saying "not recorded" — importing it would write an entry
    /// that claims nothing.</summary>
    public const int MinLevel = 1;
    public const int MaxLevel = 5;

    /// <summary>Whole minutes, matching SeizureEntry.DurationMinutes. The sheet itself
    /// only keeps a parsed duration when minutes > 0, so 0 is not a value the app can
    /// hold — a sub-minute seizure rounds up to 1 and the exact wording belongs in the
    /// note.</summary>
    public const int MinSeizureMinutes = 1;
    public const int MaxSeizureMinutes = 24 * 60;

    public const decimal MaxWeightKg = 500m;
    public const decimal MaxGlucose = 100m;
    public const decimal MaxGrams = 20_000m;
    public const decimal MaxMilliliters = 20_000m;

    /// <summary>A custom tracker's unit is the owner's own word, so there is no unit to
    /// reason from and the ceiling is only here to catch a transcription slip that ran
    /// the digits together. Deliberately loose.</summary>
    public const decimal MaxCustomAmount = 1_000_000m;

    /// <summary>How far ahead of the device's clock a date may sit before it is
    /// rejected. One day, not zero: the file may have been generated in a timezone
    /// ahead of this device, and a diary entry written "tonight" is legitimate. Anything
    /// further is a mis-transcribed year, which is the single most damaging mistake an
    /// AI can make here — it buries entries decades away from the pet's history where no
    /// owner will ever find them.</summary>
    public const int MaxFutureDays = 1;

    /// <summary>Dates travel as this and nothing else — no locale ordering to guess at,
    /// and the same shape the cloud layer already uses (see CloudJson).</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>Times of day travel as 24-hour HH:mm. Optional everywhere: an omitted
    /// time means the owner's notes did not say, which the app already has an honest
    /// representation for (see <see cref="ImportPlan"/>).</summary>
    public const string TimeFormat = "HH:mm";

    // ── Species ─────────────────────────────────────────────────────────────────

    /// <summary>The canonical species keys the type picker stores. Free text is still
    /// accepted (the app stores custom types verbatim), but a file naming one of these
    /// in any casing is normalized to the canonical form so an imported pet's chip
    /// matches a hand-created one instead of sitting under "Other".</summary>
    public static IReadOnlyList<string> CanonicalSpecies { get; } =
        new[] { "Dog", "Cat", "Bird", "Rabbit", "Fish", "Other" };

    /// <summary>The canonical casing for a species, or the trimmed input unchanged when
    /// it is a custom type.</summary>
    public static string NormalizeSpecies(string? species)
    {
        var trimmed = (species ?? string.Empty).Trim();
        foreach (var known in CanonicalSpecies)
            if (string.Equals(known, trimmed, StringComparison.OrdinalIgnoreCase))
                return known;
        return trimmed;
    }

    // ── Conditions ──────────────────────────────────────────────────────────────

    /// <summary>The condition ids a file may name, read from the app's own catalog so
    /// the two can never disagree. The empty id ("None / Not sure") is excluded: a file
    /// says nothing by omitting the array, never by naming emptiness.</summary>
    public static IReadOnlyList<string> ConditionIds { get; } =
        ConditionCatalog.Conditions
            .Select(c => c.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();

    public static bool IsKnownCondition(string? id) =>
        ConditionIds.Any(known => string.Equals(known, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The catalog's own casing for a condition id.</summary>
    public static string NormalizeCondition(string id) =>
        ConditionIds.First(known => string.Equals(known, id.Trim(), StringComparison.OrdinalIgnoreCase));
}
