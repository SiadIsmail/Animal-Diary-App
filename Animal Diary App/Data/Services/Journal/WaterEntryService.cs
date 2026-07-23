namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>Reads and writes the two water stores behind one seam:
/// <list type="bullet">
/// <item><see cref="WaterAmountEntry"/> — exact millilitre readings, additive like
///   glucose (many per day, never upserted; the report sums them per day).</item>
/// <item><see cref="WaterLevelEntry"/> — the relative reading, one per day like
///   appetite (re-logging replaces the day's row; a tombstone is revived, not
///   duplicated).</item>
/// </list>
/// Both can coexist for a day. Mirrors <see cref="GlucoseEntryService"/> (amounts)
/// and <see cref="AppetiteEntryService"/> (levels).</summary>
public class WaterEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public WaterEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    // ── Exact amounts (additive events) ──────────────────────────────────────────

    public async Task<int> InsertAmountAsync(WaterAmountEntry entry)
    {
        entry.Date = entry.Date.Date;
        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Soft delete an amount reading (the undo path) — the row becomes a
    /// tombstone so the deletion can sync (see <see cref="ISyncable"/>).</summary>
    public async Task DeleteAmountAsync(int id)
    {
        var row = await _db.Table<WaterAmountEntry>()
            .Where(w => w.Id == id && w.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<WaterAmountEntry>> GetAmountsForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<WaterAmountEntry>()
            .Where(w => w.PetId == petId && w.Date == day && w.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(w => w.Time).ToList();
    }

    public async Task<List<WaterAmountEntry>> GetAmountsForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<WaterAmountEntry>()
            .Where(w => w.PetId == petId && w.Date >= start && w.Date <= end && w.IsDeleted == false)
            .ToListAsync();
    }

    // ── Relative level (one per day) ─────────────────────────────────────────────

    public async Task<int> InsertLevelAsync(WaterLevelEntry entry)
    {
        entry.Date = entry.Date.Date;

        // One-per-day meets sync: if the day's row was soft-deleted (an undone log),
        // revive it in place instead of inserting a sibling — the cloud keys the
        // level by (pet, day), so a day must stay a single level row.
        var day = entry.Date;
        var tombstone = (await _db.Table<WaterLevelEntry>()
                .Where(w => w.PetId == entry.PetId && w.Date == day && w.IsDeleted == true)
                .ToListAsync())
            .FirstOrDefault();
        if (tombstone != null)
        {
            tombstone.Time = entry.Time;
            tombstone.Level = entry.Level;
            tombstone.IsDeleted = false;
            await _db.UpdateAsync(SyncStamp.Touch(tombstone));
            return tombstone.Id;
        }

        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Overwrite the day's level in place (the one-per-day replace path).</summary>
    public Task UpdateLevelAsync(WaterLevelEntry entry) => _db.UpdateAsync(SyncStamp.Touch(entry));

    /// <summary>Soft delete the day's level reading (the undo path) — a tombstone so
    /// the deletion syncs; a later re-log of the same day revives it (see InsertLevelAsync).</summary>
    public async Task DeleteLevelAsync(int id)
    {
        var row = await _db.Table<WaterLevelEntry>()
            .Where(w => w.Id == id && w.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<WaterLevelEntry>> GetLevelsForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<WaterLevelEntry>()
            .Where(w => w.PetId == petId && w.Date == day && w.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(w => w.Time).ToList();
    }

    public async Task<List<WaterLevelEntry>> GetLevelsForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<WaterLevelEntry>()
            .Where(w => w.PetId == petId && w.Date >= start && w.Date <= end && w.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>Whether the pet has ever logged any water (either store) — cheap gate
    /// for the export sheet's water toggles (only shown when there's water to include).</summary>
    public async Task<bool> HasAnyAsync(int petId)
    {
        var amounts = await _db.Table<WaterAmountEntry>()
            .Where(w => w.PetId == petId && w.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (amounts != null)
            return true;
        var levels = await _db.Table<WaterLevelEntry>()
            .Where(w => w.PetId == petId && w.IsDeleted == false)
            .FirstOrDefaultAsync();
        return levels != null;
    }
}
