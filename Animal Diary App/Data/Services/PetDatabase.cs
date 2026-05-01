namespace Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using SQLite;
public class PetDatabase
{
    private SQLiteAsyncConnection database;

    public async Task Init()
    {
        if (database != null)
            return;

        database = new SQLiteAsyncConnection("pets.db");
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