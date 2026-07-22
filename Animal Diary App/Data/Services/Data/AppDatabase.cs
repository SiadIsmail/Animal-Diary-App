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
            _db.CreateTableAsync<Medication>(),
            _db.CreateTableAsync<PetEntry>(),
            _db.CreateTableAsync<AppSettings>(),
            _db.CreateTableAsync<MedicationSchedule>(),
            _db.CreateTableAsync<ReminderInstance>(),
            _db.CreateTableAsync<MedicationDoseLog>(),
            _db.CreateTableAsync<Tracker>(),
            _db.CreateTableAsync<PetCondition>(),
            _db.CreateTableAsync<GlucoseEntry>(),
            _db.CreateTableAsync<AppetiteEntry>(),
            _db.CreateTableAsync<SeizureEntry>(),
            _db.CreateTableAsync<VetReportFile>()
            );
    }

}