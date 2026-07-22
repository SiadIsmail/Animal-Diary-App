namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Reads and writes <see cref="GlucoseEntry"/> rows. These are never upserted —
/// each reading is its own row, so a day can hold several. Mirrors the other data
/// services' shape.
/// </summary>
public class GlucoseEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public GlucoseEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>Insert one reading; returns the new row id (kept for undo).</summary>
    public async Task<int> InsertAsync(GlucoseEntry entry)
    {
        entry.Date = entry.Date.Date;
        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Remove one reading by id (the undo path). Soft delete — the row
    /// becomes a tombstone so the deletion can sync (see <see cref="ISyncable"/>).</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await _db.Table<GlucoseEntry>()
            .Where(g => g.Id == id && g.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    /// <summary>Every reading for a pet on a date, earliest first.</summary>
    public async Task<List<GlucoseEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<GlucoseEntry>()
            .Where(g => g.PetId == petId && g.Date == day && g.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(g => g.Time).ToList();
    }

    /// <summary>All readings for a pet within an inclusive date range (for the
    /// pending engine's per-day counts and the week-strip rose dots).</summary>
    public async Task<List<GlucoseEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<GlucoseEntry>()
            .Where(g => g.PetId == petId && g.Date >= start && g.Date <= end && g.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>The most recent reading for a pet (any day), or null if none — used
    /// to pre-fill the stepper so the owner nudges from their last number.</summary>
    public async Task<GlucoseEntry?> GetMostRecentAsync(int petId)
    {
        // Order by date in SQL and pull only a small slice — a long-lived diabetic
        // pet accumulates ~1,000 rows/year and this runs on every sheet open. The
        // slice comfortably covers the latest day's readings; the Time tie-break
        // happens in memory on those few rows.
        var rows = await _db.Table<GlucoseEntry>()
            .Where(g => g.PetId == petId && g.IsDeleted == false)
            .OrderByDescending(g => g.Date)
            .Take(24)
            .ToListAsync();
        return rows
            .OrderByDescending(g => g.Date)
            .ThenByDescending(g => g.Time)
            .FirstOrDefault();
    }
}
