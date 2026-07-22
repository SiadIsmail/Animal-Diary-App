namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>Reads and writes <see cref="AppetiteEntry"/> rows. Appetite is one
/// reading per day (like Mood and Weight): logging it again replaces the day's row
/// via <see cref="UpdateAsync"/> rather than adding a duplicate.</summary>
public class AppetiteEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public AppetiteEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public async Task<int> InsertAsync(AppetiteEntry entry)
    {
        entry.Date = entry.Date.Date;

        // One-per-day meets sync: if the day's row was soft-deleted (an undone
        // log), revive it in place instead of inserting a sibling — the cloud
        // keys appetite by (pet, day), so a day must stay a single row.
        var day = entry.Date;
        var tombstone = (await _db.Table<AppetiteEntry>()
                .Where(a => a.PetId == entry.PetId && a.Date == day && a.IsDeleted == true)
                .ToListAsync())
            .FirstOrDefault();
        if (tombstone != null)
        {
            tombstone.Time = entry.Time;
            tombstone.Level = entry.Level;
            tombstone.IsDeleted = false;
            await _db.UpdateAsync(SyncStamp.Touch(tombstone));
            return tombstone.Id;
        }

        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Overwrite an existing reading in place (the one-per-day replace path).</summary>
    public Task UpdateAsync(AppetiteEntry entry) => _db.UpdateAsync(SyncStamp.Touch(entry));

    /// <summary>Soft delete (the undo path) — the row becomes a tombstone so the
    /// deletion can sync; a later re-log of the same day revives it (see InsertAsync).</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await _db.Table<AppetiteEntry>()
            .Where(a => a.Id == id && a.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<AppetiteEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date == day && a.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(a => a.Time).ToList();
    }

    public async Task<List<AppetiteEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date >= start && a.Date <= end && a.IsDeleted == false)
            .ToListAsync();
    }
}
