namespace Animal_Diary_App.Data.Services.Cloud;

using Animal_Diary_App.Data.Models;

/// <summary>Typed access to the <see cref="SyncState"/> key-value table — the sync
/// engine's cursors, flags, and account breadcrumbs. Key vocabulary lives in
/// <see cref="CloudSyncService"/>; nothing else writes this table.</summary>
public sealed class SyncStateStore
{
    private readonly AppDatabase _db;

    public SyncStateStore(AppDatabase db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string key)
        => (await _db.Connection.Table<SyncState>().Where(s => s.Key == key).FirstOrDefaultAsync())?.Value;

    public Task SetAsync(string key, string value)
        => _db.Connection.InsertOrReplaceAsync(new SyncState { Key = key, Value = value });

    public Task RemoveAsync(string key)
        => _db.Connection.Table<SyncState>().DeleteAsync(s => s.Key == key);
}
