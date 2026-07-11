namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Reads and writes <see cref="TrackingEntry"/> rows — the generic per-item
/// values logged on the Calendar. Mirrors <see cref="PetEntryService"/>'s shape.
/// </summary>
public class TrackingEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public TrackingEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>All logged tracking values for a pet on a single date.</summary>
    public async Task<List<TrackingEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var d = date.Date;
        return await _db.Table<TrackingEntry>()
            .Where(e => e.PetId == petId && e.Date == d)
            .ToListAsync();
    }

    /// <summary>Insert or update one (pet, date, item) value; returns the row id.
    /// The caller keeps the id so the next save updates the same row.</summary>
    public async Task<int> UpsertAsync(TrackingEntry entry)
    {
        entry.Date = entry.Date.Date;
        if (entry.Id > 0)
            await _db.UpdateAsync(entry);
        else
            await _db.InsertAsync(entry);
        return entry.Id;
    }
}
