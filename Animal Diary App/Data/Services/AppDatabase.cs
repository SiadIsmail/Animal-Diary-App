using SQLite;
using Animal_Diary_App.Data.Models;
public class AppDatabase
{
    private readonly SQLiteAsyncConnection _db;
    private Task? _initializeTask;

    public AppDatabase()
    {
        var path = Path.Combine(FileSystem.AppDataDirectory, "appdata.db");
        _db = new SQLiteAsyncConnection(path);
    }

    public SQLiteAsyncConnection Connection => _db;

    public Task EnsureInitializedAsync()
    {
        return _initializeTask ??= InitAsync();
    }

    private async Task InitAsync()
    {
        await Task.WhenAll(
            _db.CreateTableAsync<Pet>(),
            _db.CreateTableAsync<MedicationLog>(),
            _db.CreateTableAsync<PetEntry>());
    }
}