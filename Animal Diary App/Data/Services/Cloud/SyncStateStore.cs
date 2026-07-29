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

    /// <summary>
    /// Delete every key under a prefix. This is the account-teardown primitive: all
    /// account-scoped sync state is <c>cloud:</c>-prefixed, so one call cannot miss a key
    /// that some later feature added.
    ///
    /// <para>That mattered: a stale <c>cloud:cursor:*</c> outliving its account is what let
    /// a device sign out of one account, into another, and back — and never see the first
    /// account's pets again, because every cursor sat ahead of their rows.</para>
    ///
    /// <para>Prefixes are literal here; <c>cloud:</c> contains no SQL <c>LIKE</c> wildcard.
    /// Keep it that way rather than adding an ESCAPE clause for a caller that doesn't exist.</para>
    /// </summary>
    public Task ClearPrefixAsync(string prefix)
        => _db.Connection.ExecuteAsync("delete from \"SyncState\" where Key like ?", prefix + "%");
}
