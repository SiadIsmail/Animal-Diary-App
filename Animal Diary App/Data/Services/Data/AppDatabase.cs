namespace Animal_Diary_App.Data.Services;

using SQLite;
using Animal_Diary_App.Data.Models;

/// <summary>
/// Owns the one SQLite connection and creates every table exactly once.
/// Everything else in the app reaches the database through a repository in
/// <c>Services/Data</c> or <c>Services/Journal</c>, never through this type
/// directly: see AI/coding-standards.md.
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
        // Every table the app has, from the one registry: a new table is a line in
        // SyncedTables, not an edit here (see that file for why).
        //
        // ONE transaction rather than nineteen concurrent CreateTableAsync calls. That
        // Task.WhenAll looked like parallelism and wasn't: sqlite-net's async API queues
        // each call to the THREAD POOL and then serializes them all on the one shared
        // connection, so it occupied nineteen pooled threads to do one thing at a time,
        // on the cold-start path, where the pool is still at its minimum size and has to
        // inject threads one at a time to satisfy them. Same work, one hop.
        await _db.RunInTransactionAsync(conn =>
        {
            foreach (var table in SyncedTables.Everything)
                table.CreateTable(conn);
        });

        // Rows written before the sync columns existed carry NULLs in them, and
        // NULL breaks both filters (`IsDeleted = 0` excludes NULL: every old row
        // would vanish from every read) and identity (no SyncId). Normalize once,
        // idempotently, before anything queries. See ISyncable / docs/history/CLOUD_SYNC_PLAN.md.
        await _db.RunInTransactionAsync(conn =>
        {
            foreach (var table in SyncedTables.All)
                table.BackfillSyncColumns(conn);

            // Same NULL story, one column, one table: Pet.IsDemo is additive, so every pet
            // written before demo mode existed holds NULL. The sync guard is phrased as a
            // set membership test and so reads NULL correctly on its own (see
            // PetScopeSql.ExcludesDemo): this normalizes the column anyway, so that any
            // future reader is free to write the obvious `IsDemo = 0` and be right.
            conn.Execute("update \"Pet\" set IsDemo = 0 where IsDemo is null");
        });
    }
}
