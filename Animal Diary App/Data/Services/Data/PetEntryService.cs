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
}