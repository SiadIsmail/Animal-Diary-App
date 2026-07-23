namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>Reads and writes the two appetite stores behind one seam (mirrors
/// <see cref="WaterEntryService"/>):
/// <list type="bullet">
/// <item><see cref="AppetiteEntry"/> — the qualitative reading, one per day
///   (re-logging replaces the day's row; a tombstone is revived, not duplicated).</item>
/// <item><see cref="AppetiteAmountEntry"/> — exact measured grams, additive like
///   glucose (many per day, never upserted; the report sums them per day).</item>
/// </list>
/// Both carry an optional free-text <c>Food</c> label.</summary>
public class AppetiteEntryService
{
    private readonly SQLiteAsyncConnection _db;

    public AppetiteEntryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    // ── Qualitative level (one per day) ──────────────────────────────────────────

    public async Task<int> InsertAsync(AppetiteEntry entry)
    {
        entry.Date = entry.Date.Date;

        // One-per-day meets sync: if the day's row was soft-deleted (an undone log),
        // revive it in place instead of inserting a sibling — the cloud keys appetite
        // by (pet, day), so a day must stay a single row.
        var day = entry.Date;
        var tombstone = (await _db.Table<AppetiteEntry>()
                .Where(a => a.PetId == entry.PetId && a.Date == day && a.IsDeleted == true)
                .ToListAsync())
            .FirstOrDefault();
        if (tombstone != null)
        {
            tombstone.Time = entry.Time;
            tombstone.Level = entry.Level;
            tombstone.Food = entry.Food;
            tombstone.IsDeleted = false;
            await _db.UpdateAsync(SyncStamp.Touch(tombstone));
            return tombstone.Id;
        }

        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Overwrite an existing reading in place (the one-per-day replace path).</summary>
    public Task UpdateAsync(AppetiteEntry entry) => _db.UpdateAsync(SyncStamp.Touch(entry));

    /// <summary>Soft delete (the undo path) — the row becomes a tombstone so the
    /// deletion can sync; a later re-log of the same day revives it (see InsertAsync).</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await _db.Table<AppetiteEntry>()
            .Where(a => a.Id == id && a.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<AppetiteEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date == day && a.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(a => a.Time).ToList();
    }

    public async Task<List<AppetiteEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.Date >= start && a.Date <= end && a.IsDeleted == false)
            .ToListAsync();
    }

    // ── Exact grams (additive events) ────────────────────────────────────────────

    public async Task<int> InsertAmountAsync(AppetiteAmountEntry entry)
    {
        entry.Date = entry.Date.Date;
        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Soft delete an amount reading (the undo path).</summary>
    public async Task DeleteAmountAsync(int id)
    {
        var row = await _db.Table<AppetiteAmountEntry>()
            .Where(a => a.Id == id && a.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    public async Task<List<AppetiteAmountEntry>> GetAmountsForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<AppetiteAmountEntry>()
            .Where(a => a.PetId == petId && a.Date == day && a.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(a => a.Time).ToList();
    }

    public async Task<List<AppetiteAmountEntry>> GetAmountsForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<AppetiteAmountEntry>()
            .Where(a => a.PetId == petId && a.Date >= start && a.Date <= end && a.IsDeleted == false)
            .ToListAsync();
    }

    // ── Food context helpers ─────────────────────────────────────────────────────

    /// <summary>The pet's most recently logged non-empty food label (across both
    /// stores), for pre-filling the sheet. Empty when the pet has never named a food.
    /// Not food-change tracking — just "remember what I last typed".</summary>
    public async Task<string> GetLastFoodAsync(int petId)
    {
        var lastLevel = (await _db.Table<AppetiteEntry>()
                .Where(a => a.PetId == petId && a.IsDeleted == false && a.Food != "")
                .ToListAsync())
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.Time)
            .FirstOrDefault();
        var lastAmount = (await _db.Table<AppetiteAmountEntry>()
                .Where(a => a.PetId == petId && a.IsDeleted == false && a.Food != "")
                .ToListAsync())
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.Time)
            .FirstOrDefault();

        if (lastLevel == null) return lastAmount?.Food ?? string.Empty;
        if (lastAmount == null) return lastLevel.Food;
        // Both exist → whichever is newer.
        var levelKey = lastLevel.Date + lastLevel.Time;
        var amountKey = lastAmount.Date + lastAmount.Time;
        return amountKey >= levelKey ? lastAmount.Food : lastLevel.Food;
    }

    /// <summary>Whether the pet has ever logged any appetite (either store) — the
    /// export sheet's appetite toggles only show when there's data to include.</summary>
    public async Task<bool> HasAnyAsync(int petId)
    {
        var level = await _db.Table<AppetiteEntry>()
            .Where(a => a.PetId == petId && a.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (level != null)
            return true;
        var amount = await _db.Table<AppetiteAmountEntry>()
            .Where(a => a.PetId == petId && a.IsDeleted == false)
            .FirstOrDefaultAsync();
        return amount != null;
    }
}
