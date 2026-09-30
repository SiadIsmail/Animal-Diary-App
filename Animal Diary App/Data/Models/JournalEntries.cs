namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  Typed Journal entries.
//
//  These sit ALONGSIDE the existing entry stores (mood + weight on PetEntry;
//  scheduled doses on MedicationDoseLog). Glucose and Seizure allow MANY rows per
//  day, so each reading is an event with its own time, never overwriting the last.
//  Appetite is the exception: one reading per day (like Mood + Weight), so re-logging
//  replaces the day's row rather than adding another.
//
//  Design rule: imperfection on the frame, never on the readout. Values here are
//  stored precisely (glucose to its exact decimal; appetite as the raw level) and
//  are formatted, never rounded, at display time.
//
//  All three share the (PetId, Date) shape of the rest of the schema so a day's
//  entries are one indexed range read. Date is date-only for day grouping; Time is
//  the time-of-day the reading was taken, shown on the timeline.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Whether a glucose reading was taken before or after food: the single
/// most important bit of context for interpreting the number.</summary>
[StoreAsText]
public enum FoodContext
{
    BeforeFood,
    AfterFood
}

/// <summary>One blood-glucose reading. Multiple per day are expected (a PerDay
/// tracker), so these are never upserted: each reading is its own row.</summary>
public class GlucoseEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_Glucose_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    /// <summary>Date-only, for counting the day's readings against the tracker's
    /// PerDayCount and for timeline grouping.</summary>
    [Indexed(Name = "IX_Glucose_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    /// <summary>Time of day the reading was taken; rendered exactly on the timeline.</summary>
    public TimeSpan Time { get; set; }

    /// <summary>The reading, stored to its exact value in mmol/L whatever the owner
    /// typed. What they typed is in <see cref="Unit"/>; the display unit is derived from
    /// their entries and is neither of those (see <c>DisplayUnitResolver</c>).</summary>
    public decimal Value { get; set; }

    /// <summary>
    /// The unit the owner typed the reading in ("mmol_l", "mg_dl"): see
    /// <see cref="UnitCatalog"/>. <b>The stored value stays CANONICAL (mmol/L);
    /// this is provenance</b>, so the app can show the owner their own majority unit
    /// back and reopen an edit in the unit it was written in, while every aggregate
    /// keeps reading one comparable number.
    ///
    /// <para><b>Null means mmol/L</b>, true of every row written before units
    /// existed: no backfill needed. SQLite.NET adds the column automatically.</para>
    /// </summary>
    public string? Unit { get; set; }

    public FoodContext Context { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Appetite: like water, TWO modes across TWO stores (see WaterAmountEntry /
//  WaterLevelEntry for the identical shape and the reasons):
//
//   • AppetiteEntry      : the qualitative reading (Didn't eat … Everything),
//     ONE per day, replace-on-relog. The default mode.
//   • AppetiteAmountEntry: an exact measured amount of food in grams, ADDITIVE
//     like glucose (many per day, never upserted; the report sums per day).
//
//  Both carry an OPTIONAL Food string: free-text context ("chicken kibble"),
//  never a food entity or change-tracking. Both can coexist on a day.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The day's qualitative appetite reading: one per day (like Mood + Weight).
/// Stored as the raw 1–5 level; the word is resolved for display (see
/// <see cref="AppetiteLevelExtensions"/>). A number is never shown to the owner.</summary>
public class AppetiteEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_Appetite_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_Appetite_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>1..5: see <see cref="AppetiteLevel"/>. Stored as the int; displayed
    /// as the matching word, never as "3/5".</summary>
    public int Level { get; set; }

    /// <summary>Optional free-text food context ("chicken kibble"), or empty. Never a
    /// food entity: just a label the owner can attach and the report can list.</summary>
    public string Food { get; set; } = string.Empty;
}

/// <summary>One exact measured food amount in grams. Additive: many per day are
/// expected (a meal each), so never upserted; each is its own row and the report sums
/// them per day. Mirrors <see cref="WaterAmountEntry"/>.</summary>
public class AppetiteAmountEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_AppetiteAmount_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_AppetiteAmount_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>The amount eaten in grams, stored exactly.</summary>
    public decimal Grams { get; set; }

    /// <summary>
    /// The unit the owner typed the reading in ("g", "oz"): see
    /// <see cref="UnitCatalog"/>. <b>The stored value stays CANONICAL (grams);
    /// this is provenance</b>, so the app can show the owner their own majority unit
    /// back and reopen an edit in the unit it was written in, while every aggregate
    /// keeps reading one comparable number.
    ///
    /// <para><b>Null means grams</b>, true of every row written before units
    /// existed: no backfill needed. SQLite.NET adds the column automatically.</para>
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>Optional free-text food context ("chicken kibble"), or empty.</summary>
    public string Food { get; set; } = string.Empty;
}

/// <summary>One seizure occurrence. A complete seizure diary is one of the most
/// useful things an owner can hand a vet, so this captures when it happened, how
/// long it lasted, and anything noticed, while it's still fresh. Logged from the
/// "+" sheet (an Event tracker), never nagged for.</summary>
public class SeizureEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_Seizure_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_Seizure_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>
    /// <b>DEAD COLUMN. Never read, never written.</b> How long the seizure lasted, in
    /// whole minutes: the original store, replaced by <see cref="DurationSeconds"/>.
    ///
    /// <para>An <c>int</c> of minutes cannot hold a 45-second seizure, and sub-minute
    /// events are common and clinically relevant, so this was not a usability wrinkle but
    /// data loss: the owner typed 45 seconds and the record kept "0" or "1". Existing
    /// values were migrated (×60) by the idempotent backfill in
    /// <c>AppDatabase.InitAsync</c>.</para>
    ///
    /// <para>It stays on the entity because sqlite-net never drops a column, and because
    /// a device still running an older build pushes <c>duration_minutes</c> and nothing
    /// else: keeping the property is what lets that value arrive and be backfilled on the
    /// next launch rather than vanishing. See AI/known-constraints.md.</para>
    /// </summary>
    public int? DurationMinutes { get; set; }

    /// <summary>
    /// How long it lasted, <b>in seconds</b>. Null when the owner didn't time it, which
    /// is a normal answer and not a skipped field.
    ///
    /// <para>Seconds is the canonical unit because it is how owners describe seizures:
    /// "about forty seconds", not "about 0.7 minutes". The sheet offers seconds and
    /// minutes and defaults to seconds.</para>
    /// </summary>
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// The unit the owner typed the duration in ("s", "min"): see
    /// <see cref="UnitCatalog"/> and <see cref="UnitFamily.Duration"/>.
    ///
    /// <para><b>The stored value stays CANONICAL (seconds); this is provenance</b>, so
    /// the app can show the owner their own majority unit back while every aggregate
    /// keeps reading one comparable number.</para>
    ///
    /// <para><b>Null means seconds.</b> That is also true of every row the ×60 backfill
    /// converted: a value recorded in minutes is now held in seconds, and the unit the
    /// owner typed it in is genuinely unknown for those rows, so claiming "min" would be
    /// inventing provenance the record never had.</para>
    /// </summary>
    public string? Unit { get; set; }

    /// <summary>What kind it was, or NULL when the owner didn't say, which is the
    /// resting state and a normal answer, not a skipped field. There is deliberately no
    /// "Unknown" member: an owner who doesn't know picks nothing, and the app never
    /// infers a type from the duration, the note, or anything else.</summary>
    public SeizureType? Type { get; set; }

    public string Note { get; set; } = string.Empty;
}

/// <summary>
/// How a seizure presented: the vet's own vocabulary, because this exists to be read
/// at an appointment. The app only ever records the owner's answer; nothing suggests,
/// derives or second-guesses it (AI/app-voice.md §16).
///
/// <b>Values are pinned and must never be reordered.</b> These are stored as the INT,
/// not as text, and not by choice: sqlite-net reads <c>[StoreAsText]</c> off the
/// property's declared type, so on a nullable enum (<c>SeizureType?</c> is
/// <c>Nullable&lt;SeizureType&gt;</c>) the attribute is never found and the column
/// silently becomes an integer anyway. Pinning the numbers makes that storage safe
/// instead of a trap. The CLOUD column is text (see SyncTableMaps): the member names
/// are the wire format there, so renaming one orphans every synced row.
/// </summary>
public enum SeizureType
{
    /// <summary>Whole body.</summary>
    Generalized = 1,

    /// <summary>Stayed in one part of the body.</summary>
    Focal = 2,

    /// <summary>Started in one part, then became generalized.</summary>
    FocalToGeneralized = 3
}

public static class SeizureTypeExtensions
{
    /// <summary>The localized term for a stored type (EN + DE via AppStrings). One set of
    /// words for the sheet, the timeline and the vet report: the owner and the vet must
    /// never be shown different names for the same answer.</summary>
    public static string GetDisplayName(this SeizureType type) =>
        Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString($"Journal_SeizureType{type}");

    /// <summary>Same, for the nullable field: empty when the owner didn't say.</summary>
    public static string GetDisplayName(this SeizureType? type) =>
        type is SeizureType t ? t.GetDisplayName() : string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Water intake: TWO independent stores, because the two modes have different
//  shapes (and a table can carry only one cloud conflict key):
//
//   • WaterAmountEntry: exact millilitre readings, ADDITIVE like glucose: many
//     per day, each its own event, never upserted. The owner's choice how to
//     split it: four 100 ml sips or one 400 ml bowl both land as the same daily
//     total (the report sums per day).
//   • WaterLevelEntry: the quick relative reading, one per day like appetite:
//     re-logging replaces the day's row.
//
//  Both can coexist for one day; nothing forces the owner to pick a single mode.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One exact water reading in millilitres. Additive: many per day are
/// expected (the owner logs each drink), so these are never upserted; each is its
/// own row and the report sums them per day. Mirrors <see cref="GlucoseEntry"/>.</summary>
public class WaterAmountEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_WaterAmount_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_WaterAmount_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>The reading in millilitres, stored exactly.</summary>
    public decimal AmountMl { get; set; }

    /// <summary>
    /// The unit the owner typed the reading in ("ml", "fl_oz", "cup"): see
    /// <see cref="UnitCatalog"/>. <b>The stored value stays CANONICAL (millilitres);
    /// this is provenance</b>, so the app can show the owner their own majority unit
    /// back and reopen an edit in the unit it was written in, while every aggregate
    /// keeps reading one comparable number.
    ///
    /// <para><b>Null means millilitres</b>, true of every row written before units
    /// existed: no backfill needed. SQLite.NET adds the column automatically.</para>
    /// </summary>
    public string? Unit { get; set; }
}

/// <summary>The day's relative water reading: one per day (like Appetite + Mood +
/// Weight). Stored as the raw 1–5 level; the word is resolved for display (see
/// <see cref="WaterLevelExtensions"/>). A number is never shown to the owner.</summary>
public class WaterLevelEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed(Name = "IX_WaterLevel_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_WaterLevel_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>1..5: see <see cref="WaterLevel"/>. Stored as the int; displayed as
    /// the matching word, never "3/5".</summary>
    public int Level { get; set; }
}

/// <summary>The five relative water-intake levels: the quick mode for owners who
/// don't measure the bowl. The owner sees the WORD; the app stores the int. As with
/// appetite, there is deliberately no "3/5" anywhere, and no judgement (a low reading
/// is a neutral fact).</summary>
public enum WaterLevel
{
    None = 0,
    Barely = 1,
    ALittle = 2,
    Normal = 3,
    MoreThanUsual = 4,
    ALot = 5
}

public static class WaterLevelExtensions
{
    /// <summary>The localized word for a stored level (EN + DE via AppStrings). The
    /// owner always sees the word, never the number.</summary>
    public static string GetDisplayName(this WaterLevel level)
    {
        if (level is < WaterLevel.Barely or > WaterLevel.ALot)
            return string.Empty;
        return Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString($"Water_Level{(int)level}");
    }

    /// <summary>Fraction of the drop/glass to fill for the level's indicator (0..1).
    /// Used by the water sheet's progressively-filled glass, no numbers shown.</summary>
    public static double GlassFill(this WaterLevel level) =>
        level == WaterLevel.None ? 0 : (int)level / 5.0;
}

/// <summary>The five appetite levels. The owner sees the WORD; the app stores the
/// int. There is deliberately no "3/5" anywhere.</summary>
public enum AppetiteLevel
{
    None = 0,
    Barely = 1,
    ALittle = 2,
    AboutHalf = 3,
    MostOfIt = 4,
    Everything = 5
}

public static class AppetiteLevelExtensions
{
    /// <summary>The localized word for a stored level (EN + DE via AppStrings). The
    /// owner always sees the word, never the number.</summary>
    public static string GetDisplayName(this AppetiteLevel level)
    {
        if (level is < AppetiteLevel.Barely or > AppetiteLevel.Everything)
            return string.Empty;
        return Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString($"Appetite_Level{(int)level}");
    }

    /// <summary>Fraction of the bowl to fill for the level's indicator (0..1). Used by
    /// the appetite sheet's progressively-filled bowl, no numbers shown.</summary>
    public static double BowlFill(this AppetiteLevel level) =>
        level == AppetiteLevel.None ? 0 : (int)level / 5.0;
}
