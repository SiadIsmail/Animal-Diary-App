namespace Animal_Diary_App.Data.Services;

using SQLite;
using Animal_Diary_App.Data.Models;

/// <summary>
/// Owns the one SQLite connection and creates every table exactly once.
/// Everything else in the app reaches the database through a repository in
/// <c>Services/Data</c> or <c>Services/Journal</c>, never through this type
/// directly — see AI/coding-standards.md.
/// </summary>
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
        // Every table the app has, from the one registry — a new table is a line in
        // SyncedTables, not an edit here (see that file for why).
        await Task.WhenAll(SyncedTables.Everything.Select(t => t.CreateTableAsync(_db)));

        // Rows written before the sync columns existed carry NULLs in them, and
        // NULL breaks both filters (`IsDeleted = 0` excludes NULL — every old row
        // would vanish from every read) and identity (no SyncId). Normalize once,
        // idempotently, before anything queries. See ISyncable / docs/history/CLOUD_SYNC_PLAN.md.
        await _db.RunInTransactionAsync(conn =>
        {
            foreach (var table in SyncedTables.All)
                table.BackfillSyncColumns(conn);
        });
    }
}
