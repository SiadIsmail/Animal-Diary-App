using SQLite;
using Animal_Diary_App.Data.Models;
public class AppDatabase
{
    private readonly SQLiteAsyncConnection _db;
    public AppDatabase()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
        _db = new SQLiteAsyncConnection(path);
    }

    public SQLiteAsyncConnection Connection => _db;

    public async Task InitAsync()
    {
        await _db.CreateTableAsync<Pet>();
        await _db.CreateTableAsync<MedicationLog>();
        await _db.CreateTableAsync<PetEntry>();
    }
}