namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

using SQLite;

using System.Diagnostics;
public class PetService
{
    private readonly SQLiteAsyncConnection _db;

    public PetService(AppDatabase database)
    {
        _db = database.Connection;
    }



    public async Task SavePetAsync(Pet pet)
    {
        await _db.InsertAsync(SyncStamp.Touch(pet));
    }

    public async Task UpdatePetAsync(Pet pet)
    {
        await _db.UpdateAsync(SyncStamp.Touch(pet));
    }

    public async Task<List<Pet>> GetPetsAsync()
    {
        return await _db.Table<Pet>()
            .Where(p => p.IsDeleted == false)
            .ToListAsync();
    }

    public async Task<Pet> GetPetByIdAsync(int id)
    {
        return await _db.Table<Pet>()
            .Where(p => p.Id == id && p.IsDeleted == false)
            .FirstOrDefaultAsync();
    }
}
