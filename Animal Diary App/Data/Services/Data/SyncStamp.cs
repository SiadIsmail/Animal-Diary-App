namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The single place sync-tracking columns are written (see <see cref="ISyncable"/>).
/// Repositories route every insert/update through <see cref="Touch"/> and every
/// delete of a synced row through <see cref="MarkDeleted"/> — centralized so no
/// write path can forget a stamp. Returns the row so calls compose inline:
/// <c>await _db.InsertAsync(SyncStamp.Touch(entry))</c>.
/// </summary>
public static class SyncStamp
{
    /// <summary>Raised on every stamped write. This single hook is how the cloud
    /// sync layer notices local changes (it debounces a sync behind it) — no
    /// repository ever talks to the sync engine directly. No-op when nothing
    /// subscribes (cloud off / signed out).</summary>
    public static event Action? RowTouched;

    /// <summary>Stamp a row as locally written: ensure it has a global identity,
    /// record the write time (UTC, for last-write-wins), and queue it for upload.</summary>
    public static T Touch<T>(T row) where T : ISyncable
    {
        if (string.IsNullOrEmpty(row.SyncId))
            row.SyncId = Guid.NewGuid().ToString();
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.IsDirty = true;
        RowTouched?.Invoke();
        return row;
    }

    /// <summary>Soft-delete a row: mark it deleted and stamp the write. The caller
    /// still persists it with an Update — the row must remain in the table as a
    /// tombstone so the deletion can reach the cloud (and other devices).</summary>
    public static T MarkDeleted<T>(T row) where T : ISyncable
    {
        row.IsDeleted = true;
        return Touch(row);
    }
}
