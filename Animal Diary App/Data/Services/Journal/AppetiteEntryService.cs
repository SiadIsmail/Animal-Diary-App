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
        await _db.InsertAsync(entry);
        return entry.Id;
    }

    /// <summary>Overwrite an existing reading in place (the one-per-day replace path).</summary>
    public Task UpdateAsync(AppetiteEntry entry) => _db.UpdateAsync(entry);

    public Task DeleteAsync(int id) => _db.DeleteAsync<AppetiteEntry>(id);

    public async Task<List<AppetiteEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date == day)
            .ToListAsync();
        return rows.OrderBy(a => a.Time).ToList();
    }

    public async Task<List<AppetiteEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date >= start && a.Date <= end)
            .ToListAsync();
    }
}
