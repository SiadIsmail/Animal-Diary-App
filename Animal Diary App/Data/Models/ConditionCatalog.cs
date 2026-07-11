namespace Animal_Diary_App.Data.Models;

/// <summary>
/// The single source of truth mapping each <see cref="Condition"/> to the
/// <see cref="TrackingItem"/>s a pet with that condition logs.
///
/// To add a new condition later:
///   1. add a <see cref="Condition"/> to <see cref="Conditions"/>,
///   2. add any new items it needs to <see cref="Items"/> (or reuse existing ones),
///   3. add its id → item-ids entry to <see cref="ByCondition"/>.
/// No UI changes are required — the picker and Calendar read from here.
/// </summary>
public static class ConditionCatalog
{
    /// <summary>Conditions shown in the picker, in display order. The empty id is
    /// the gentle "None / Not sure" default.</summary>
    public static IReadOnlyList<Condition> Conditions { get; } = new List<Condition>
    {
        new("",             "None / Not sure",        "🌿"),
        new("diabetes",     "Diabetes",               "🩸"),
        new("ckd",          "Kidney disease (CKD)",   "💧"),
        new("epilepsy",     "Epilepsy / Seizures",    "⚡"),
        new("heart",        "Heart disease",          "🫀"),
        new("hyperthyroid", "Hyperthyroidism",        "🦋"),
    };

    /// <summary>Every tracking item keyed by id so conditions can share them
    /// (e.g. "water" and "vomiting" appear under more than one condition).</summary>
    private static readonly Dictionary<string, TrackingItem> Items = new()
    {
        // ── General items (always apply). Weight, Mood and Medication are already
        // logged by the Calendar's existing UI, so they're IsNative and the
        // dynamic tracker skips them — they live here only to document intent. ──
        ["weight"]   = new("weight",   "Weight",         InputKind.Numeric, "⚖️", unit: "kg", isNative: true),
        ["mood"]     = new("mood",     "Mood / Energy",  InputKind.Scale,   "😊",             isNative: true),
        ["meds"]     = new("meds",     "Medication given", InputKind.Dose,  "💊",             isNative: true),
        ["appetite"] = new("appetite", "Appetite",       InputKind.Scale,   "🍽️"),

        // ── Condition-specific items ──
        ["insulin"]     = new("insulin",     "Insulin dose",              InputKind.Dose,    "💉"),
        ["glucose"]     = new("glucose",     "Blood glucose",             InputKind.Numeric, "🩸", unit: "mg/dL"),
        ["water"]       = new("water",       "Water intake",              InputKind.Volume,  "💧", unit: "ml"),
        ["vomiting"]    = new("vomiting",    "Vomiting",                  InputKind.Boolean, "🤢"),
        ["subq"]        = new("subq",        "Sub-Q fluids given",        InputKind.Volume,  "💦", unit: "ml"),
        ["seizure"]     = new("seizure",     "Seizure",                   InputKind.Event,   "⚡"),
        ["seizurenote"] = new("seizurenote", "Post-seizure notes",        InputKind.Text,    "📝"),
        ["resprate"]    = new("resprate",    "Resting respiratory rate",  InputKind.Numeric, "🫁", unit: "breaths/min"),
        ["breathing"]   = new("breathing",   "Breathing / cough",         InputKind.Scale,   "🌬️"),
    };

    /// <summary>Items that apply to every pet, whatever their condition. (Notes was
    /// folded into the Mood tracker, so it's no longer a standalone general item.)</summary>
    private static readonly string[] GeneralItemIds = { "weight", "mood", "appetite", "meds" };

    /// <summary>Extra items per condition id (general items are added on top).</summary>
    private static readonly Dictionary<string, string[]> ByCondition = new()
    {
        [""]             = System.Array.Empty<string>(),
        ["diabetes"]     = new[] { "insulin", "glucose", "water" },
        ["ckd"]          = new[] { "water", "vomiting", "subq" },
        ["epilepsy"]     = new[] { "seizure", "seizurenote" },
        ["heart"]        = new[] { "resprate", "breathing" },
        ["hyperthyroid"] = new[] { "vomiting" }, // Weight + Appetite already come from the general set
    };

    /// <summary>Condition metadata for an id, falling back to "None / Not sure".</summary>
    public static Condition GetCondition(string? conditionId) =>
        Conditions.FirstOrDefault(c => c.Id == (conditionId ?? string.Empty)) ?? Conditions[0];

    /// <summary>General + condition-specific items in display order, de-duplicated.
    /// Includes native items — callers rendering the dynamic section should use
    /// <see cref="GetDynamicTrackingItems"/> instead.</summary>
    public static IReadOnlyList<TrackingItem> GetTrackingItems(string? conditionId)
    {
        var result = new List<TrackingItem>();
        var seen = new HashSet<string>();

        void Add(string id)
        {
            if (seen.Add(id) && Items.TryGetValue(id, out var item))
                result.Add(item);
        }

        foreach (var id in GeneralItemIds)
            Add(id);

        if (conditionId != null && ByCondition.TryGetValue(conditionId, out var extra))
            foreach (var id in extra)
                Add(id);

        return result;
    }

    /// <summary>The items the Calendar renders dynamically: everything except the
    /// ones already handled by the native Weight / Mood / Medication UI.</summary>
    public static IReadOnlyList<TrackingItem> GetDynamicTrackingItems(string? conditionId) =>
        GetTrackingItems(conditionId).Where(i => !i.IsNative).ToList();

    /// <summary>General (always-on) items rendered as their own Tracker Hub rows —
    /// i.e. non-native general items such as Appetite. Weight/Mood/Medication are
    /// native and get bespoke hub rows instead.</summary>
    public static IReadOnlyList<TrackingItem> GetGeneralDynamicItems() =>
        GeneralItemIds.Select(id => Items[id]).Where(i => !i.IsNative).ToList();

    /// <summary>The condition-specific items for one condition (non-native), used as
    /// the children of that condition's Tracker Hub group. Excludes the general
    /// items, which live at the hub root.</summary>
    public static IReadOnlyList<TrackingItem> GetConditionItems(string? conditionId)
    {
        var ids = conditionId != null && ByCondition.TryGetValue(conditionId, out var extra)
            ? extra
            : System.Array.Empty<string>();
        return ids.Where(Items.ContainsKey).Select(id => Items[id]).Where(i => !i.IsNative).ToList();
    }
}
