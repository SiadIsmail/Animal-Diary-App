namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>Reads and writes <see cref="SeizureEntry"/> rows. Seizures are an Event
/// tracker — never pending — logged from the "+" sheet. Mirrors the other Journal
/// entry services.</summary>
public class SeizureEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public SeizureEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public async Task<int> InsertAsync(SeizureEntry entry)
    {
        entry.Date = entry.Date.Date;
        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Soft delete (the undo path) — the row becomes a tombstone so the
    /// deletion can sync (see <see cref="ISyncable"/>).</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await _db.Table<SeizureEntry>()
            .Where(s => s.Id == id && s.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<SeizureEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<SeizureEntry>()
            .Where(s => s.PetId == petId && s.Date == day && s.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(s => s.Time).ToList();
    }

    /// <summary>All seizures for a pet within an inclusive date range (for the vet
    /// report's event list and per-week counts). Mirrors the other entry services.</summary>
    public async Task<List<SeizureEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<SeizureEntry>()
            .Where(s => s.PetId == petId && s.Date >= start && s.Date <= end && s.IsDeleted == false)
            .ToListAsync();
    }
}
