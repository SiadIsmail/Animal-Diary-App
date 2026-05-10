namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;
using System.Diagnostics;
public class PetDatabase
{
    private SQLiteAsyncConnection database;

    public async Task Init()
    {
        if (database != null)
            return;
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "pets.db");
        database = new SQLiteAsyncConnection(dbPath);
        await database.CreateTableAsync<Pet>();
    }

    public async Task SavePetAsync(Pet pet)
    {
        await Init();
        await database.InsertAsync(pet);
    }

    public async Task<List<Pet>> GetPetsAsync()
    {
        await Init();
        return await database.Table<Pet>().ToListAsync();
    }
}