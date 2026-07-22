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
        await _db.InsertAsync(SyncStamp.Touch(PetEntry));
    }

    public async Task UpdatePetEntryAsync(PetEntry PetEntry)
    {
        await _db.UpdateAsync(SyncStamp.Touch(PetEntry));
    }

    /// <summary>Most recent weight entry for a pet, or null if it has none.
    /// Pushes the ordering into SQL (backed by the (PetId, Date) index) instead of
    /// loading the whole table and sorting in memory.</summary>
    public async Task<PetEntry?> GetLatestWeightEntryAsync(int petId)
    {
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.Weight > 0 && e.IsDeleted == false)
            .OrderByDescending(e => e.Date)
            .FirstOrDefaultAsync();
    }
    /// <summary>Most recent mood entry for a pet, or null if it has none. Mirrors
    /// <see cref="GetLatestWeightEntryAsync"/> — the ordering runs in SQL rather than
    /// loading the table and sorting in memory.</summary>
    public async Task<PetEntry?> GetLatestMoodEntryAsync(int petId)
    {
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.MoodLevel > 0 && e.IsDeleted == false)
            .OrderByDescending(e => e.Date)
            .FirstOrDefaultAsync();
    }

    public async Task<PetEntry> GetPetEntryByDateAndPetIdAsync(DateTime date, int petId)
    {
        return await _db.Table<PetEntry>()
            .Where(e => e.Date == date && e.PetId == petId && e.IsDeleted == false)
            .FirstOrDefaultAsync();
    }

    /// <summary>All entries for a pet within an inclusive date range (date-only).
    /// Used to render month-grid activity dots (weight + mood) in one query.</summary>
    public async Task<List<PetEntry>> GetPetEntriesByPetIdAndRangeAsync(int petId, DateTime start, DateTime end)
    {
        var from = start.Date;
        var to = end.Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.Date >= from && e.Date <= to && e.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>Weight entries for a pet over the last <paramref name="days"/> days
    /// (date-ascending). Powers the weight-trend chart's range selector.</summary>
    public async Task<List<PetEntry>> GetWeightEntriesForRangeAsync(int petId, int days)
    {
        var from = DateTime.Now.AddDays(-days).Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.Weight > 0 && e.Date >= from && e.IsDeleted == false)
            .OrderBy(e => e.Date)
            .ToListAsync();
    }

    public async Task<List<PetEntry>> GetLast30DaysMoodEntriesAsync(int petId)
    {
        var thirtyDaysAgo = DateTime.Now.AddDays(-30).Date;
        return await _db.Table<PetEntry>()
            .Where(e => e.PetId == petId && e.MoodLevel > 0 && e.Date >= thirtyDaysAgo && e.IsDeleted == false)
            .OrderBy(e => e.Date)
            .ToListAsync();
    }
}
