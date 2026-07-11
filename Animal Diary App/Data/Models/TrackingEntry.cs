namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// One logged value for one <see cref="TrackingItem"/>, for a pet on a date.
/// A single row holds any input kind via nullable typed columns — only the
/// column(s) relevant to the item's <see cref="InputKind"/> are set:
///   Numeric / Scale / Volume → <see cref="Number"/>
///   Boolean / Dose (given?)  → <see cref="Flag"/>
///   Text / Event note        → <see cref="Text"/>
///   Dose / Event time-of-day → <see cref="TimeTicks"/>
///   Event                    → <see cref="DurationSeconds"/> + <see cref="Severity"/>
///
/// There is at most one row per (PetId, Date, ItemId) — the tracker upserts.
/// (Multiple same-day events, e.g. two seizures, would be a future extension.)
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

    /// <summary>The <see cref="TrackingItem.Id"/> this value belongs to.</summary>
    public string ItemId { get; set; } = string.Empty;

    public double? Number { get; set; }
    public bool? Flag { get; set; }
    public string? Text { get; set; }

    /// <summary>Time-of-day ticks (a <see cref="System.TimeSpan"/>) for Dose / Event.</summary>
    public long? TimeTicks { get; set; }
    public int? DurationSeconds { get; set; }
    public int? Severity { get; set; }
}
