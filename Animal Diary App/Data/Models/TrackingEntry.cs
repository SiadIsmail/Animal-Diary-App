namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// LEGACY, read-only in practice: one value logged by the removed Tracker Hub,
/// for a pet on a date, keyed by a string item id ("seizure", "vomiting", …).
/// Nothing writes this table anymore, but the vet report still READS it for
/// historical data — the table, service and DI registration must stay.
/// A single row held any input kind via nullable typed columns:
///   numeric / scale / volume → <see cref="Number"/>
///   boolean / dose (given?)  → <see cref="Flag"/>
///   text / event note        → <see cref="Text"/>
///   dose / event time-of-day → <see cref="TimeTicks"/>
///   event                    → <see cref="DurationSeconds"/> + <see cref="Severity"/>
/// At most one row per (PetId, Date, ItemId) — the hub upserted.
/// </summary>
public class TrackingEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Mirrors PetEntry's composite index: every read filters by pet + date.
    [Indexed(Name = "IX_TrackingEntry_Pet_Date", Order = 1)]
    public int PetId { get; set; }
    [Indexed(Name = "IX_TrackingEntry_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    /// <summary>The removed hub's stable item key ("seizure", "vomiting", …).</summary>
    public string ItemId { get; set; } = string.Empty;

    public double? Number { get; set; }
    public bool? Flag { get; set; }
    public string? Text { get; set; }

    /// <summary>Time-of-day ticks (a <see cref="System.TimeSpan"/>) for Dose / Event.</summary>
    public long? TimeTicks { get; set; }
    public int? DurationSeconds { get; set; }
    public int? Severity { get; set; }
}
