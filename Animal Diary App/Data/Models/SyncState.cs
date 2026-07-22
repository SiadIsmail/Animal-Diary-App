namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// Key-value store for the sync engine's own state — per-table download cursors
/// (the last server <c>updated_at</c> seen) and, later, account info so Settings
/// can render without a network call. One row per key; the sync engine owns the
/// key vocabulary. Deliberately schemaless (string value) so Phase 1 can evolve
/// its cursor format without a migration. Never synced itself; wiped by the full
/// data reset like every other table.
/// </summary>
public class SyncState
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
