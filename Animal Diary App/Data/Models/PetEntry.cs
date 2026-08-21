namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// The day's mood and weight for one pet: the two always-on trackers, sharing a row
/// because they share a day. Everything else the Journal records has its own table
/// (see JournalEntries.cs).
///
/// <para>Split out of Pet.cs, where it used to live: <see cref="Pet"/> resolves a photo
/// path through <c>PetPhotoService</c>, which makes that file unlinkable by the MAUI-free
/// test project, and this entity has no such dependency.</para>
/// </summary>
public class PetEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    // Composite index on (PetId, Date): every entry query filters by pet and a
    // date range, so this covers the weight-chart, mood-timeline and day-lookup
    // reads with a single B-tree instead of a full table scan.
    [Indexed(Name = "IX_PetEntry_Pet_Date", Order = 1)]
    public int PetId { get; set; }
    [Indexed(Name = "IX_PetEntry_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }
    public string Mood { get; set; } = string.Empty;
    public int MoodLevel { get; set; } = 0;

    /// <summary>Optional free-text note the owner writes alongside the mood (the
    /// journal's washi-tape card shows it). Added when Notes was folded into the
    /// Mood tracker. SQLite.NET adds this column automatically, no migration.</summary>
    public string MoodNote { get; set; } = string.Empty;

    /// <summary>Whether the owner asked for this day's <see cref="MoodNote"/> to
    /// appear in the vet report's Owner's Notes section. Defaults to false, so
    /// notes are private to the app unless the owner opts in per note; legacy
    /// entries written before this column read as false. SQLite.NET adds the
    /// column automatically, no migration.</summary>
    public bool IncludeInVetReport { get; set; }
    public decimal Weight { get; set; }

    /// <summary>Time-of-day (ticks) the mood was logged, or null for entries written
    /// before per-entry times existed. Lets the chronological Journal timeline place
    /// mood at the moment it was recorded. SQLite.NET adds this column automatically.</summary>
    public long? MoodTimeTicks { get; set; }

    /// <summary>Time-of-day (ticks) the weight was logged, or null for legacy entries.
    /// Weight and mood carry separate times because they're logged independently.</summary>
    public long? WeightTimeTicks { get; set; }
}
