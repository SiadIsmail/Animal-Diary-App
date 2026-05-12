namespace Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using SQLite;

public class PetEntryDatabase
{
    private SQLiteAsyncConnection database;
    public async Task Init()
    {
        if (database != null)
            return;

        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "petsentries.db");
        database = new SQLiteAsyncConnection(dbPath);
        await database.CreateTableAsync<PetEntry>();
    }

    public async Task SavePetEntryAsync(PetEntry PetEntry)
    {
        await Init();
        await database.InsertAsync(PetEntry);
    }

    public async Task UpdatePetEntryAsync(PetEntry PetEntry)
    {
        await Init();
        await database.UpdateAsync(PetEntry);
    }

    public async Task<List<PetEntry>> GetPetEntriesAsync()
    {
        await Init();
        return await database.Table<PetEntry>().ToListAsync();
    }

    public async Task<PetEntry> GetPetEntryByDateAsync(DateTime date)
    {
        await Init();
        return await database.Table<PetEntry>().Where(e => e.Date == date).FirstOrDefaultAsync();
    }

}