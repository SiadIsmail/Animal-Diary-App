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
        await _db.InsertAsync(pet);
    }

    public async Task<List<Pet>> GetPetsAsync()
    {
        return await _db.Table<Pet>().ToListAsync();
    }
}