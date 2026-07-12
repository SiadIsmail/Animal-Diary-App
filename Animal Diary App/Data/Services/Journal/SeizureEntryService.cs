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
        await _db.InsertAsync(entry);
        return entry.Id;
    }

    public Task DeleteAsync(int id) => _db.DeleteAsync<SeizureEntry>(id);

    public async Task<List<SeizureEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<SeizureEntry>()
            .Where(s => s.PetId == petId && s.Date == day)
            .ToListAsync();
        return rows.OrderBy(s => s.Time).ToList();
    }
}
