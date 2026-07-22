namespace Animal_Diary_App.Data.Models;

/// <summary>
/// The four sync-tracking columns carried by every entity that will reach the
/// cloud (see CLOUD_SYNC_PLAN.md). Local <c>int Id</c> stays the primary key and
/// every existing query keeps working; these columns exist only for the sync
/// layer:
///
/// <list type="bullet">
/// <item><see cref="SyncId"/> — the row's global identity: a GUID assigned locally
/// at insert (or by the one-time backfill in <c>AppDatabase</c>). The cloud keys
/// rows by it; local foreign keys stay plain ints and are translated at the sync
/// boundary.</item>
/// <item><see cref="UpdatedAtUtc"/> — when the row was last written, used for
/// last-write-wins conflict resolution. NOT used to detect local changes (clock
/// changes would make that unreliable) — that's what <see cref="IsDirty"/> is for.</item>
/// <item><see cref="IsDirty"/> — true = written locally and not yet pushed. The
/// upload queue is simply <c>WHERE IsDirty = 1</c>; the sync engine clears it
/// after a successful push. Meaningless (and harmless) while cloud is off.</item>
/// <item><see cref="IsDeleted"/> — soft delete. Synced tables never hard-delete
/// (a vanished row can't be propagated); repositories mark this instead and every
/// read filters it. Hard deletes remain only for the full data reset and for
/// never-synced tables (<c>ReminderInstance</c>, <c>VetReportFile</c>, <c>AppSettings</c>).</item>
/// </list>
///
/// Rows are stamped exclusively through <c>SyncStamp.Touch</c>/<c>MarkDeleted</c> —
/// never set these by hand in a repository, so no write path can forget one.
/// </summary>
public interface ISyncable
{
    /// <summary>The local autoincrement primary key every entity already has —
    /// exposed here so the sync engine can address rows generically.</summary>
    int Id { get; set; }

    string SyncId { get; set; }
    DateTime UpdatedAtUtc { get; set; }
    bool IsDirty { get; set; }
    bool IsDeleted { get; set; }
}
