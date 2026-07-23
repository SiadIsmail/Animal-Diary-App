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

/// <summary>Whether a glucose reading was taken before or after food — the single
/// most important bit of context for interpreting the number.</summary>
[StoreAsText]
public enum FoodContext
{
    BeforeFood,
    AfterFood
}

/// <summary>One blood-glucose reading. Multiple per day are expected (a PerDay
/// tracker), so these are never upserted — each reading is its own row.</summary>
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

    /// <summary>The reading, stored to its exact value (one decimal place in the UI).
    /// The unit lives on the glucose <see cref="Tracker"/> (mmol/L today).</summary>
    public decimal Value { get; set; }

    public FoodContext Context { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Appetite — like water, TWO modes across TWO stores (see WaterAmountEntry /
//  WaterLevelEntry for the identical shape and the reasons):
//
//   • AppetiteEntry       — the qualitative reading (Didn't eat … Everything),
//     ONE per day, replace-on-relog. The default mode.
//   • AppetiteAmountEntry — an exact measured amount of food in grams, ADDITIVE
//     like glucose (many per day, never upserted; the report sums per day).
//
//  Both carry an OPTIONAL Food string — free-text context ("chicken kibble"),
//  never a food entity or change-tracking. Both can coexist on a day.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The day's qualitative appetite reading — one per day (like Mood + Weight).
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

    /// <summary>1..5 — see <see cref="AppetiteLevel"/>. Stored as the int; displayed
    /// as the matching word, never as "3/5".</summary>
    public int Level { get; set; }

    /// <summary>Optional free-text food context ("chicken kibble"), or empty. Never a
    /// food entity — just a label the owner can attach and the report can list.</summary>
    public string Food { get; set; } = string.Empty;
}

/// <summary>One exact measured food amount in grams. Additive — many per day are
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

    /// <summary>Optional free-text food context ("chicken kibble"), or empty.</summary>
    public string Food { get; set; } = string.Empty;
}

/// <summary>One seizure occurrence. A complete seizure diary is one of the most
/// useful things an owner can hand a vet, so this captures when it happened, how
/// long it lasted, and anything noticed — while it's still fresh. Logged from the
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

    /// <summary>How long it lasted, in minutes. Null when the owner didn't time it.</summary>
    public int? DurationMinutes { get; set; }

    public string Note { get; set; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Water intake — TWO independent stores, because the two modes have different
//  shapes (and a table can carry only one cloud conflict key):
//
//   • WaterAmountEntry — exact millilitre readings, ADDITIVE like glucose: many
//     per day, each its own event, never upserted. The owner's choice how to
//     split it — four 100 ml sips or one 400 ml bowl both land as the same daily
//     total (the report sums per day).
//   • WaterLevelEntry — the quick relative reading, one per day like appetite:
//     re-logging replaces the day's row.
//
//  Both can coexist for one day; nothing forces the owner to pick a single mode.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One exact water reading in millilitres. Additive — many per day are
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

    /// <summary>The reading in millilitres, stored exactly. The unit ("ml") is
    /// implicit; the water <see cref="Tracker"/> carries it for symmetry with glucose.</summary>
    public decimal AmountMl { get; set; }
}

/// <summary>The day's relative water reading — one per day (like Appetite + Mood +
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

    /// <summary>1..5 — see <see cref="WaterLevel"/>. Stored as the int; displayed as
    /// the matching word, never "3/5".</summary>
    public int Level { get; set; }
}

/// <summary>The five relative water-intake levels — the quick mode for owners who
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
    /// Used by the water sheet's progressively-filled glass — no numbers shown.</summary>
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
    /// the appetite sheet's progressively-filled bowl — no numbers shown.</summary>
    public static double BowlFill(this AppetiteLevel level) =>
        level == AppetiteLevel.None ? 0 : (int)level / 5.0;
}
