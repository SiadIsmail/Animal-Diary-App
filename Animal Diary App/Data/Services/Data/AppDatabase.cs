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
            _db.CreateTableAsync<AppetiteAmountEntry>(),
            _db.CreateTableAsync<SeizureEntry>(),
            _db.CreateTableAsync<WaterAmountEntry>(),
            _db.CreateTableAsync<WaterLevelEntry>(),
            _db.CreateTableAsync<VetReportFile>(),
            _db.CreateTableAsync<SyncState>()
            );

        // Rows written before the sync columns existed carry NULLs in them, and
        // NULL breaks both filters (`IsDeleted = 0` excludes NULL — every old row
        // would vanish from every read) and identity (no SyncId). Normalize once,
        // idempotently, before anything queries. See ISyncable / CLOUD_SYNC_PLAN.md.
        await _db.RunInTransactionAsync(BackfillSyncColumns);
    }

    private static void BackfillSyncColumns(SQLiteConnection conn)
    {
        Backfill<Pet>(conn);
        Backfill<PetEntry>(conn);
        Backfill<Medication>(conn);
        Backfill<MedicationSchedule>(conn);
        Backfill<MedicationDoseLog>(conn);
        Backfill<Tracker>(conn);
        Backfill<PetCondition>(conn);
        Backfill<GlucoseEntry>(conn);
        Backfill<AppetiteEntry>(conn);
        Backfill<AppetiteAmountEntry>(conn);
        Backfill<SeizureEntry>(conn);
        Backfill<WaterAmountEntry>(conn);
        Backfill<WaterLevelEntry>(conn);
    }

    private static void Backfill<T>(SQLiteConnection conn) where T : ISyncable, new()
    {
        var table = conn.GetMapping<T>().TableName;

        // NULL → the columns' pre-sync defaults. UpdatedAtUtc 0 = DateTime.MinValue,
        // which any real edit beats in last-write-wins. IsDirty stays false — the
        // enable-cloud migration marks everything dirty explicitly when the user
        // opts in, so nothing queues for upload before an account exists.
        conn.Execute($"update \"{table}\" set IsDeleted = 0 where IsDeleted is null");
        conn.Execute($"update \"{table}\" set IsDirty = 0 where IsDirty is null");
        conn.Execute($"update \"{table}\" set UpdatedAtUtc = 0 where UpdatedAtUtc is null");

        // Assign the global identity to pre-existing rows. GUIDs must be generated
        // per row in C# (SQLite has no uuid()), but after the first launch this
        // query returns nothing and the whole backfill is a no-op.
        var missing = conn.Query<T>($"select * from \"{table}\" where SyncId is null or SyncId = ''");
        foreach (var row in missing)
        {
            row.SyncId = Guid.NewGuid().ToString();
            conn.Update(row);
        }
    }
}
