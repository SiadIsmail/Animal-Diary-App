namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  Typed Journal entries.
//
//  These sit ALONGSIDE the existing entry stores (mood + weight on PetEntry;
//  scheduled doses on MedicationDoseLog). Unlike the generic TrackingEntry — which
//  upserts a single row per (pet, date, item) — each of these allows MANY rows per
//  day, so a reading is an event with its own time, never overwriting the last.
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
public class GlucoseEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

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

/// <summary>One appetite reading for the day. Stored as the raw 1–5 level; the word
/// is resolved for display (see <see cref="AppetiteLevelExtensions"/>). A number is
/// never shown to the owner.</summary>
public class AppetiteEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_Appetite_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_Appetite_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>1..5 — see <see cref="AppetiteLevel"/>. Stored as the int; displayed
    /// as the matching word, never as "3/5".</summary>
    public int Level { get; set; }
}

/// <summary>One seizure occurrence. A complete seizure diary is one of the most
/// useful things an owner can hand a vet, so this captures when it happened, how
/// long it lasted, and anything noticed — while it's still fresh. Logged from the
/// "+" sheet (an Event tracker), never nagged for.</summary>
public class SeizureEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed(Name = "IX_Seizure_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_Seizure_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>How long it lasted, in minutes. Null when the owner didn't time it.</summary>
    public int? DurationMinutes { get; set; }

    public string Note { get; set; } = string.Empty;
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
