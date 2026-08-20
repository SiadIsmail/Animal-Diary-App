namespace Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The two Today stat cards.
//
//  Today shows exactly TWO cards, and the owner decides what each one holds. The
//  point is personalization, not a dashboard: two slots, one record each, no
//  layout to arrange and nothing to switch off.
//
//  Every card states the LAST RECORDED value and when it was taken — never a
//  score, a streak, or a count of days without something (see the seizure rule in
//  AI/design-decisions.md → "Felova records; it never judges"). "Seizure free for
//  12 days" is a streak the owner can break by writing down the truth, so it does
//  not exist anywhere in this feature.
//
//  Cards are the existing trackers plus medication doses, so nothing new is
//  modelled: a card is a view onto data the Journal already collects.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which record a Today stat card shows. Six of these are the pet's own
/// <see cref="Models.TrackerId"/> values; <see cref="Medication"/> is the odd one out
/// because doses are not trackers (see <see cref="Tracker"/>) — they live in the
/// medication model and are read from the dose log.</summary>
public enum TodayCardId
{
    Weight,
    Mood,
    Glucose,
    Appetite,
    Water,
    Seizure,
    Medication
}

/// <summary>
/// <b>Which record a card holds</b>, when "which" outgrew the enum: one of the shipped
/// <see cref="TodayCardId"/> values, or a tracker the owner defined.
///
/// <para>The same tagged union <see cref="TrackerKey"/> is for the care plan, and for the
/// same reason — a single <c>TodayCardId.Custom</c> member would make every owner-defined
/// tracker the same card, so picking "Walk" for the left card would light up "Vomiting"
/// as already chosen and the swap rule would fire between two unrelated records.</para>
///
/// <para>The implicit conversion keeps every existing call site reading as it did.</para>
/// </summary>
/// <param name="BuiltIn">The shipped record this names, or null for a custom tracker.</param>
/// <param name="CustomId">The <c>CustomTracker</c> row id when <paramref name="BuiltIn"/>
/// is null; 0 otherwise.</param>
public readonly record struct TodayCardKey(TodayCardId? BuiltIn, int CustomId)
{
    public bool IsCustom => BuiltIn is null;

    public static implicit operator TodayCardKey(TodayCardId id) => new(id, 0);

    public static TodayCardKey Custom(int customTrackerId) => new(null, customTrackerId);

    public bool Is(TodayCardId id) => BuiltIn == id;

    /// <summary>The stored form: the enum's name, or <c>custom:7</c>. Round-trips through
    /// <see cref="TryParse"/>, and an old preference written before custom cards existed
    /// still parses as the built-in it always was.</summary>
    public override string ToString() => IsCustom ? $"custom:{CustomId}" : BuiltIn!.ToString()!;

    /// <summary>Parse a stored key. Anything unrecognised fails rather than guessing —
    /// the caller falls back to the pet's defaults, which is what a card removed in a
    /// later version should do.</summary>
    public static bool TryParse(string? stored, out TodayCardKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(stored))
            return false;

        const string customPrefix = "custom:";
        if (stored.StartsWith(customPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(stored[customPrefix.Length..], out var id) || id <= 0)
                return false;
            key = Custom(id);
            return true;
        }

        if (!Enum.TryParse<TodayCardId>(stored, out var builtIn))
            return false;
        key = builtIn;
        return true;
    }
}

/// <summary>Which of the two cards is being talked about. Left and right, nothing more —
/// there is deliberately no third slot and no ordering to manage.</summary>
public enum TodayCardSlot
{
    Primary,
    Secondary
}

/// <summary>What the two Today cards currently show for one pet. A value type: the
/// owner's choice is two enum values, so it persists as two words and can never hold
/// a half-configured state.</summary>
public readonly record struct TodayCardConfig(TodayCardKey Primary, TodayCardKey Secondary)
{
    public TodayCardKey For(TodayCardSlot slot) =>
        slot == TodayCardSlot.Primary ? Primary : Secondary;

    /// <summary>Put <paramref name="card"/> in <paramref name="slot"/>.
    ///
    /// <para>If the other slot already holds it the two <b>swap</b> rather than
    /// duplicate — the same record must never occupy both cards, and refusing the pick
    /// instead would leave an owner unable to reorder the pair at all.</para></summary>
    public TodayCardConfig With(TodayCardSlot slot, TodayCardKey card)
    {
        var other = For(slot == TodayCardSlot.Primary ? TodayCardSlot.Secondary : TodayCardSlot.Primary);
        var displaced = other == card ? For(slot) : other;

        return slot == TodayCardSlot.Primary
            ? new TodayCardConfig(card, displaced)
            : new TodayCardConfig(displaced, card);
    }
}

/// <summary>
/// <b>The one card → (tracker, icon, label) table</b>, mirroring
/// <see cref="TrackerVisuals"/>: which records may sit on a Today card, how each
/// reads, and which pair a pet starts with.
///
/// <para>Adding a card type is one row in <see cref="Cards"/> plus one branch in
/// <c>TodayCardService.GetReadingAsync</c> — nothing in the page, the sheet or the
/// persistence has to change.</para>
/// </summary>
public static class TodayCardCatalog
{
    /// <summary>One card type. <paramref name="Tracker"/> is null only for
    /// <see cref="TodayCardId.Medication"/>, which is not a tracker.</summary>
    /// <param name="LabelKey">AppStrings key for the card's name. Every one of them is
    /// framed as "Last …" — the card reports a record, never a running total.</param>
    /// <param name="EmptyKey">AppStrings key for "nothing recorded yet". Weight and mood
    /// keep the lines those two cards already shipped with; the rest follow the same
    /// shape — state the absence, name where to write one, judge nothing. A seizure card
    /// with no entries must never read as an achievement.</param>
    /// <param name="RecordKey">AppStrings key for the record's NAME on its own
    /// ("Glucose"), as opposed to <paramref name="LabelKey"/>'s "Last glucose". The card
    /// face reports a last reading and is worded for it; a heading over a stretch of
    /// history is not about the last one, and "Last glucose" above ninety days of counts
    /// would say something untrue. Two keys, one row, so they cannot drift.</param>
    public readonly record struct TodayCardMeta(
        TodayCardId Id, TrackerId? Tracker, string LabelKey, string EmptyKey, string RecordKey);

    /// <summary>Every card type, in the order the picker sheet offers them: the two
    /// every pet can use first, then the condition-shaped ones.</summary>
    public static IReadOnlyList<TodayCardMeta> Cards { get; } = new List<TodayCardMeta>
    {
        new(TodayCardId.Weight,     TrackerId.Weight,   "Today_CardWeight",     "Main_WeightEmptyCard",  "Today_RecordWeight"),
        new(TodayCardId.Mood,       TrackerId.Mood,     "Today_CardMood",       "Main_MoodEmptyCard",    "Today_RecordMood"),
        new(TodayCardId.Glucose,    TrackerId.Glucose,  "Today_CardGlucose",    "Today_EmptyGlucose",    "Today_RecordGlucose"),
        new(TodayCardId.Appetite,   TrackerId.Appetite, "Today_CardAppetite",   "Today_EmptyAppetite",   "Today_RecordAppetite"),
        new(TodayCardId.Water,      TrackerId.Water,    "Today_CardWater",      "Today_EmptyWater",      "Today_RecordWater"),
        new(TodayCardId.Seizure,    TrackerId.Seizure,  "Today_CardSeizure",    "Today_EmptySeizure",    "Today_RecordSeizure"),
        new(TodayCardId.Medication, null,               "Today_CardMedication", "Today_EmptyMedication", "Today_RecordMedication"),
    };

    /// <summary>Doses are not trackers, so medication carries its own visual here rather
    /// than being smuggled into <see cref="TrackerVisuals"/>. Honey is the colour the
    /// Today next-up card already uses for a dose.</summary>
    private static readonly TrackerVisual MedicationVisual =
        new("💊", "Today_CardMedication", "HoneyWarmTint", "HoneyWarmTint", "HoneyDeep");

    /// <summary>The card's "nothing recorded yet" line, resolved now, never cached. A
    /// custom tracker has no catalog row, so it gets the one generic line — its NAME
    /// carries the identity, and that is owner text the catalog must never hold.</summary>
    public static string EmptyText(TodayCardKey card) =>
        Helpers.LocalizationManager.Instance.GetString(
            card.IsCustom ? "Today_EmptyCustom" : Meta(card.BuiltIn!.Value).EmptyKey);

    /// <summary>A card's row. An id with no row falls back to the first card rather
    /// than throwing — the same posture as <see cref="TrackerVisuals.Fallback"/>.</summary>
    public static TodayCardMeta Meta(TodayCardId card)
    {
        foreach (var meta in Cards)
            if (meta.Id == card)
                return meta;
        return Cards[0];
    }

    /// <summary>The tracker a card reads from, or null for medication.</summary>
    public static TrackerId? TrackerFor(TodayCardId card) => Meta(card).Tracker;

    /// <summary>Icon and tints, reusing the tracker table so a card, its Journal chip
    /// and its timeline tile can never drift apart.</summary>
    public static TrackerVisual Visual(TodayCardId card) =>
        TrackerFor(card) is TrackerId t ? TrackerVisuals.For(t) : MedicationVisual;

    /// <summary>The card's localized name ("Last seizure"), resolved now, never cached.</summary>
    public static string Label(TodayCardId card) =>
        Helpers.LocalizationManager.Instance.GetString(Meta(card).LabelKey);

    /// <summary>The record's name on its own ("Seizures") — what a stretch of history is
    /// headed with, where "Last seizure" would be a claim about the wrong thing.</summary>
    public static string RecordName(TodayCardId card) =>
        Helpers.LocalizationManager.Instance.GetString(Meta(card).RecordKey);

    // ── Defaults ──────────────────────────────────────────────────────────────
    //
    // The pair a pet starts with, chosen from its conditions — the same idea as
    // CarePlanCatalog seeding trackers from a condition, and the same vocabulary.
    // These are NOT persisted on first read: leaving them live means adding a
    // condition later improves the cards, while the first deliberate pick freezes
    // them as the owner's choice (see TodayCardService).

    /// <summary>The two records a condition makes most worth watching, most telling
    /// first. Keys match <see cref="ConditionCatalog"/> ids.</summary>
    private static readonly IReadOnlyDictionary<string, (TodayCardId First, TodayCardId Second)> ByCondition =
        new Dictionary<string, (TodayCardId, TodayCardId)>
        {
            ["diabetes"] = (TodayCardId.Glucose, TodayCardId.Weight),
            ["ckd"] = (TodayCardId.Water, TodayCardId.Appetite),
            ["epilepsy"] = (TodayCardId.Seizure, TodayCardId.Medication),
        };

    /// <summary>Where every pet lands with no condition to shape it: the weigh-in and
    /// the mood, which every care plan already tracks.</summary>
    private static readonly TodayCardId[] Fallback = { TodayCardId.Weight, TodayCardId.Mood };

    /// <summary>The starting pair for a pet carrying these conditions.
    ///
    /// <para>Each condition's most telling record comes first, so a pet with two
    /// conditions gets one card from each rather than both of the first one's; the
    /// general defaults then fill whatever is left.</para></summary>
    public static TodayCardConfig DefaultsFor(IEnumerable<string?>? conditionIds)
    {
        var ids = (conditionIds ?? System.Array.Empty<string?>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        var picked = new List<TodayCardId>(2);
        void Add(TodayCardId card)
        {
            if (picked.Count < 2 && !picked.Contains(card))
                picked.Add(card);
        }

        foreach (var id in ids)
            if (ByCondition.TryGetValue(id, out var pair)) Add(pair.First);
        foreach (var id in ids)
            if (ByCondition.TryGetValue(id, out var pair)) Add(pair.Second);
        foreach (var card in Fallback)
            Add(card);

        return new TodayCardConfig(picked[0], picked[1]);
    }
}
