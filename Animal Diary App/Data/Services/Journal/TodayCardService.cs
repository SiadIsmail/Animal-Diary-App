namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Helpers;

/// <summary>
/// One Today card's latest record, as it was written down. Deliberately
/// presentation-free: raw numbers, the stored level, the medication's own name, and
/// when it was recorded. Formatting (decimal separators, level words, "Recorded
/// yesterday") happens at display time, because a singleton holding those strings
/// would survive a live language switch in the old language.
/// </summary>
/// <param name="On">The day the record belongs to; null means nothing recorded yet —
/// that is the single "has data" test for every card.</param>
/// <param name="At">Time of day, when the entry carries one (legacy rows may not).</param>
/// <param name="Number">A measured value: kg, mmol/L, ml, grams. Null for the rest.</param>
/// <param name="Mood">The recorded mood (mood card only).</param>
/// <param name="Level">A stored 1–5 relative reading — appetite/water observations.
/// Shown as its word, never as a number.</param>
/// <param name="Text">Verbatim user data (the medication's name); never translated.</param>
/// <param name="Label">An owner-defined tracker's own name; empty for the shipped cards,
/// whose label is a localization key in <see cref="TodayCardCatalog"/>. Verbatim user
/// data, in the same category as <paramref name="Text"/> — never translated.</param>
/// <param name="Icon">That tracker's emoji; empty for the shipped cards.</param>
/// <param name="Unit">That tracker's own unit ("min", "bowls"); empty otherwise.</param>
public sealed record TodayCardReading(
    TodayCardKey Card,
    DateTime? On = null,
    TimeSpan? At = null,
    decimal? Number = null,
    MoodLevel Mood = MoodLevel.None,
    int Level = 0,
    string Text = "",
    string Label = "",
    string Icon = "",
    string Unit = "")
{
    public bool HasData => On is not null;

    /// <summary>Nothing written down for this card yet.</summary>
    public static TodayCardReading Empty(TodayCardKey card) => new(card);
}

/// <summary>
/// Backs the two customizable Today stat cards: which record each card holds for a
/// pet, and what that record currently says.
///
/// <para><b>Nothing new is stored about health here.</b> Every reading is fetched from
/// the entry store that already owns it — this service only chooses which one to ask.
/// Adding a card type is one row in <see cref="TodayCardCatalog.Cards"/> plus one
/// branch in <see cref="GetReadingAsync"/>.</para>
///
/// <para><b>The choice is a preference, not medical data</b>, so it lives in the same
/// device-scoped <c>AppSettings</c> key/value table as the language and the trial
/// flags rather than in a synced table: it needs no cloud migration, survives a sign
/// out, and is wiped by a data reset like everything else. It is keyed per pet because
/// the useful pair follows the pet's conditions — a household with a diabetic cat and
/// a healthy dog wants different cards.</para>
/// </summary>
public class TodayCardService
{
    private readonly SettingsService _settings;
    private readonly PetConditionService _conditions;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly WaterEntryService _water;
    private readonly SeizureEntryService _seizures;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly MedicationService _medications;
    private readonly CustomTrackerService _custom;

    public TodayCardService(
        SettingsService settings,
        PetConditionService conditions,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        WaterEntryService water,
        SeizureEntryService seizures,
        MedicationDoseLogService doseLogs,
        MedicationService medications,
        CustomTrackerService custom)
    {
        _settings = settings;
        _conditions = conditions;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _water = water;
        _seizures = seizures;
        _doseLogs = doseLogs;
        _medications = medications;
        _custom = custom;
    }

    // ── The owner's choice ────────────────────────────────────────────────────

    /// <summary>Pet-scoped preference key. Pet ids are local and sqlite-net's
    /// AUTOINCREMENT never reuses one, so a key can't be inherited by a later pet; a
    /// key left behind by a deleted pet is an unread ~30 bytes that the next data reset
    /// removes with the rest of AppSettings.</summary>
    private static string KeyFor(int petId) => $"TodayCards:{petId}";

    /// <summary>What this pet's two cards show.
    ///
    /// <para>Until the owner picks something, the answer is derived from the pet's
    /// conditions on every read and <b>not written down</b>. That is deliberate: adding
    /// epilepsy months later then improves the cards by itself, while the first
    /// deliberate pick freezes the pair as the owner's, exactly like a care plan that
    /// is seeded once and then owned (see AI/design-decisions.md).</para></summary>
    public async Task<TodayCardConfig> GetConfigAsync(Pet? pet)
    {
        if (pet is null || pet.Id == 0)
            return TodayCardCatalog.DefaultsFor(null);

        var saved = await _settings.GetValueAsync(KeyFor(pet.Id));
        if (TryParse(saved, out var config))
            return config;

        return TodayCardCatalog.DefaultsFor(await _conditions.GetConditionIdsAsync(pet));
    }

    /// <summary>Put a card in a slot and remember it. Returns the resulting pair, which
    /// may have swapped the two cards — see <see cref="TodayCardConfig.With"/>.</summary>
    public async Task<TodayCardConfig> SetCardAsync(Pet? pet, TodayCardSlot slot, TodayCardKey card)
    {
        var current = await GetConfigAsync(pet);
        var next = current.With(slot, card);

        if (pet is not null && pet.Id != 0)
            await _settings.SetValueAsync(KeyFor(pet.Id), $"{next.Primary}|{next.Secondary}");

        return next;
    }

    /// <summary>"Weight|Mood" or "custom:7|Mood" → the pair. Anything unparseable (a card
    /// removed in a later version, a truncated write) falls back to the condition defaults
    /// rather than showing a broken card. Preferences written before custom cards existed
    /// still parse, since a built-in key's stored form is unchanged.</summary>
    private static bool TryParse(string? stored, out TodayCardConfig config)
    {
        config = default;
        if (string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Split('|');
        if (parts.Length != 2
            || !TodayCardKey.TryParse(parts[0], out var primary)
            || !TodayCardKey.TryParse(parts[1], out var secondary)
            || primary == secondary)
            return false;

        config = new TodayCardConfig(primary, secondary);
        return true;
    }

    // ── What the card currently says ──────────────────────────────────────────

    /// <summary>The pet's most recent record for one card, or
    /// <see cref="TodayCardReading.Empty"/> when there is none. Never throws: a card
    /// that can't read its store shows its empty state, the same as a card whose store
    /// is genuinely empty.</summary>
    public async Task<TodayCardReading> GetReadingAsync(Pet? pet, TodayCardKey card)
    {
        if (pet is null || pet.Id == 0)
            return TodayCardReading.Empty(card);

        try
        {
            if (card.IsCustom)
                return await CustomAsync(pet.Id, card);

            return card.BuiltIn switch
            {
                TodayCardId.Weight => await WeightAsync(pet.Id),
                TodayCardId.Mood => await MoodAsync(pet.Id),
                TodayCardId.Glucose => await GlucoseAsync(pet.Id),
                TodayCardId.Appetite => await AppetiteAsync(pet.Id),
                TodayCardId.Water => await WaterAsync(pet.Id),
                TodayCardId.Seizure => await SeizureAsync(pet.Id),
                TodayCardId.Medication => await MedicationAsync(pet.Id),
                _ => TodayCardReading.Empty(card),
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TodayCards] reading {card} failed: {ex.Message}");
            return TodayCardReading.Empty(card);
        }
    }

    private async Task<TodayCardReading> WeightAsync(int petId)
    {
        var entry = await _petEntries.GetLatestWeightEntryAsync(petId);
        return entry is null
            ? TodayCardReading.Empty(TodayCardId.Weight)
            : new TodayCardReading(TodayCardId.Weight, entry.Date, TimeFromTicks(entry.WeightTimeTicks), Number: entry.Weight);
    }

    private async Task<TodayCardReading> MoodAsync(int petId)
    {
        var entry = await _petEntries.GetLatestMoodEntryAsync(petId);
        return entry is null
            ? TodayCardReading.Empty(TodayCardId.Mood)
            : new TodayCardReading(TodayCardId.Mood, entry.Date, TimeFromTicks(entry.MoodTimeTicks), Mood: (MoodLevel)entry.MoodLevel);
    }

    private async Task<TodayCardReading> GlucoseAsync(int petId)
    {
        var entry = await _glucose.GetMostRecentAsync(petId);
        return entry is null
            ? TodayCardReading.Empty(TodayCardId.Glucose)
            : new TodayCardReading(TodayCardId.Glucose, entry.Date, entry.Time, Number: entry.Value);
    }

    // Appetite and water each have two stores (measured / observed). The card shows
    // whichever was written LAST, in its own form — a bowl of "About half" is never
    // turned into grams, and grams are never re-labelled as a word. Two shapes, shown
    // one at a time, never merged (AI/design-decisions.md).
    private async Task<TodayCardReading> AppetiteAsync(int petId)
    {
        var amount = await _appetite.GetMostRecentAmountAsync(petId);
        var level = await _appetite.GetMostRecentAsync(petId);

        if (Newer(amount?.Date, amount?.Time, level?.Date, level?.Time))
            return new TodayCardReading(TodayCardId.Appetite, amount!.Date, amount.Time, Number: amount.Grams);

        return level is null
            ? TodayCardReading.Empty(TodayCardId.Appetite)
            : new TodayCardReading(TodayCardId.Appetite, level.Date, level.Time, Level: level.Level);
    }

    private async Task<TodayCardReading> WaterAsync(int petId)
    {
        var amount = await _water.GetMostRecentAmountAsync(petId);
        var level = await _water.GetMostRecentLevelAsync(petId);

        if (Newer(amount?.Date, amount?.Time, level?.Date, level?.Time))
            return new TodayCardReading(TodayCardId.Water, amount!.Date, amount.Time, Number: amount.AmountMl);

        return level is null
            ? TodayCardReading.Empty(TodayCardId.Water)
            : new TodayCardReading(TodayCardId.Water, level.Date, level.Time, Level: level.Level);
    }

    private async Task<TodayCardReading> SeizureAsync(int petId)
    {
        var entry = await _seizures.GetMostRecentAsync(petId);
        return entry is null
            ? TodayCardReading.Empty(TodayCardId.Seizure)
            : new TodayCardReading(TodayCardId.Seizure, entry.Date, entry.Time);
    }

    private async Task<TodayCardReading> MedicationAsync(int petId)
    {
        var log = await _doseLogs.GetMostRecentRecordedAsync(petId);
        if (log is null)
            return TodayCardReading.Empty(TodayCardId.Medication);

        // The log carries only the medication's id; the name lives on the med row, and
        // a deleted medication leaves the card with nothing to name — its empty state.
        var med = await _medications.GetMedicationByIdAsync(log.MedicationId);
        if (med is null)
            return TodayCardReading.Empty(TodayCardId.Medication);

        // The recorded moment is when the owner tapped it, falling back to the dose's
        // scheduled slot — the same rule the Journal timeline places doses by, so a dose
        // caught up the next morning reads as recorded that morning.
        var recorded = log.ResolvedAt ?? log.ScheduledDate + log.ScheduledTime;
        return new TodayCardReading(TodayCardId.Medication, recorded.Date, recorded.TimeOfDay, Text: med.Name);
    }

    // An owner-defined tracker. The definition is read from the ARCHIVED-inclusive list:
    // a card pointing at a tracker the owner has since retired must still name it and show
    // its last reading rather than going blank — retiring means "stop asking", not "forget".
    //
    // A definition that cannot be found at all (its row hard-purged with revoked cloud
    // access) yields an empty card with no name, which the card renders as its neutral
    // fallback. That is the honest outcome: there is nothing left to name.
    private async Task<TodayCardReading> CustomAsync(int petId, TodayCardKey card)
    {
        var definition = (await _custom.GetAllForPetAsync(petId))
            .FirstOrDefault(c => c.Id == card.CustomId);
        if (definition is null)
            return TodayCardReading.Empty(card);

        var icon = CustomTrackerVisuals.For(definition).Icon;
        var entry = await _custom.GetMostRecentAsync(definition.Id);
        if (entry is null)
            return new TodayCardReading(card, Label: definition.Name, Icon: icon, Unit: definition.Unit);

        // A Tick tracker has no number, so the card states WHEN it happened, exactly as
        // the seizure card does. An Amount tracker shows the value in the owner's unit.
        return new TodayCardReading(
            card, entry.Date, entry.Time,
            Number: entry.Amount,
            Label: definition.Name,
            Icon: icon,
            Unit: definition.Unit);
    }

    /// <summary>True when the first (date, time) is the later of the two; a missing
    /// first loses, a missing second wins.</summary>
    private static bool Newer(DateTime? aDate, TimeSpan? aTime, DateTime? bDate, TimeSpan? bTime)
    {
        if (aDate is null) return false;
        if (bDate is null) return true;
        return aDate.Value + (aTime ?? TimeSpan.Zero) >= bDate.Value + (bTime ?? TimeSpan.Zero);
    }

    /// <summary>Mood and weight store their time of day as nullable ticks; legacy rows
    /// written before per-entry times have none.</summary>
    private static TimeSpan? TimeFromTicks(long? ticks) =>
        ticks is long t ? TimeSpan.FromTicks(t) : null;
}
