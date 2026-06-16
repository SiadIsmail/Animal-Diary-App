namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

public class PetEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public PetEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public async Task SavePetEntryAsync(PetEntry PetEntry)
    {
        await _db.InsertAsync(PetEntry);
    }

    public async Task UpdatePetEntryAsync(PetEntry PetEntry)
    {
        await _db.UpdateAsync(PetEntry);
    }

    public async Task<List<PetEntry>> GetPetEntriesAsync()
    {
        return await _db.Table<PetEntry>().ToListAsync();
    }

    public async Task<PetEntry> GetPetEntryByDateAsync(DateTime date)
    {
        return await _db.Table<PetEntry>().Where(e => e.Date == date).FirstOrDefaultAsync();
    }
    public async Task<PetEntry> GetPetEntryByDateAndPetIdAsync(DateTime date, int petId)
    {
        return await _db.Table<PetEntry>().Where(e => e.Date == date && e.PetId == petId).FirstOrDefaultAsync();
    }

    /// <summary>All entries for a pet within an inclusive date range (date-only).
    /// Used to render month-grid activity dots (weight + mood) in one query.</summary>
    public async Task<List<PetEntry>> GetPetEntriesByPetIdAndRangeAsync(int petId, DateTime start, DateTime end)
    {
        var from = start.Date;
        var to = end.Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.Date >= from && e.Date <= to)
            .ToListAsync();
    }

    public async Task<List<PetEntry>> GetLast30DaysWeightEntriesAsync(int petId)
    {
        var thirtyDaysAgo = DateTime.Now.AddDays(-30).Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.Weight > 0 && e.Date >= thirtyDaysAgo)
            .OrderBy(e => e.Date)
            .ToListAsync();
    }

    public async Task<List<PetEntry>> GetLast30DaysMoodEntriesAsync(int petId)
    {
        var thirtyDaysAgo = DateTime.Now.AddDays(-30).Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.MoodLevel > 0 && e.Date >= thirtyDaysAgo)
            .OrderBy(e => e.Date)
            .ToListAsync();
    }
}