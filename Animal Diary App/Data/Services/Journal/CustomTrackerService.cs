namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Reads and writes the two custom-tracker stores behind one seam (mirroring
/// <see cref="WaterEntryService"/>, which holds water's two):
/// <list type="bullet">
/// <item><see cref="CustomTracker"/>: the owner's definitions.</item>
/// <item><see cref="CustomEntry"/>: their occurrences, event-shaped and append-only
///   like <see cref="GlucoseEntry"/>.</item>
/// </list>
///
/// <para><b>The day and range reads deliberately fetch EVERY custom tracker's entries
/// in one query</b> and leave the grouping to the caller. A shipped tracker costs the
/// Journal a round trip each (there are already eight in flight per day); this way ten
/// custom trackers still cost one. That is the property that makes "as many as you
/// like" affordable.</para>
/// </summary>
public class CustomTrackerService
{
    private readonly SQLiteAsyncConnection _db;

    public CustomTrackerService(AppDatabase database)
    {
        _db = database.Connection;
    }

    // ── Definitions ─────────────────────────────────────────────────────────────

    /// <summary>The pet's live trackers: what the Journal asks for and offers. Ordered
    /// by creation so the chip row doesn't reshuffle itself.</summary>
    public async Task<List<CustomTracker>> GetForPetAsync(int petId)
    {
        var rows = await _db.Table<CustomTracker>()
            .Where(c => c.PetId == petId && c.IsDeleted == false && c.IsArchived == false)
            .ToListAsync();
        return rows.OrderBy(c => c.Id).ToList();
    }

    /// <summary>Every tracker the pet has ever had, archived ones included.
    ///
    /// <para>The timeline and the vet report read THIS one: an entry outlives the
    /// retirement of the tracker that collected it, and it still needs its name and icon
    /// to render. Reading the live list there would silently blank out history the
    /// moment an owner tidied up.</para></summary>
    public async Task<List<CustomTracker>> GetAllForPetAsync(int petId)
    {
        var rows = await _db.Table<CustomTracker>()
            .Where(c => c.PetId == petId && c.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(c => c.Id).ToList();
    }

    public Task<CustomTracker?> GetByIdAsync(int id) =>
        _db.Table<CustomTracker>()
            .Where(c => c.Id == id && c.IsDeleted == false)
            .FirstOrDefaultAsync()!;

    /// <summary>How many live trackers the pet has: the gate on <see cref="CustomTracker.MaxPerPet"/>.</summary>
    public async Task<int> CountForPetAsync(int petId) => (await GetForPetAsync(petId)).Count;

    /// <summary>Insert a new definition (Id == 0) or update one in place.</summary>
    public async Task<int> SaveAsync(CustomTracker tracker)
    {
        if (tracker.Id == 0)
            await _db.InsertAsync(SyncStamp.Touch(tracker));
        else
            await _db.UpdateAsync(SyncStamp.Touch(tracker));
        return tracker.Id;
    }

    /// <summary>Retire a tracker (or bring it back). Its entries are never touched,
    /// see <see cref="CustomTracker.IsArchived"/>.</summary>
    public async Task SetArchivedAsync(int id, bool archived)
    {
        var row = await GetByIdAsync(id);
        if (row == null || row.IsArchived == archived)
            return;
        row.IsArchived = archived;
        await _db.UpdateAsync(SyncStamp.Touch(row));
    }

    /// <summary>Soft delete a definition: a tombstone, so the removal syncs.
    ///
    /// <para>This is the "I made this by mistake" path, not the "I don't do this any
    /// more" one; that is <see cref="SetArchivedAsync"/>. Entries are deliberately left
    /// alone here too: deleting logged history is the undo toast's job, one entry at a
    /// time, never a side effect of tidying the plan.</para></summary>
    public async Task DeleteAsync(int id)
    {
        var row = await GetByIdAsync(id);
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    // ── Entries (append-only events) ────────────────────────────────────────────

    public async Task<int> InsertAsync(CustomEntry entry)
    {
        entry.Date = entry.Date.Date;
        await _db.InsertAsync(SyncStamp.Touch(entry));
        return entry.Id;
    }

    /// <summary>Soft delete one entry (the ✕ + undo path): a tombstone, so it syncs.</summary>
    public async Task DeleteEntryAsync(int id)
    {
        var row = await _db.Table<CustomEntry>()
            .Where(e => e.Id == id && e.IsDeleted == false)
            .FirstOrDefaultAsync();
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    /// <summary>Every custom entry the pet logged on a day, across ALL its trackers, in
    /// time order. One query; the caller groups by <see cref="CustomEntry.CustomTrackerId"/>.</summary>
    public async Task<List<CustomEntry>> GetForDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        var rows = await _db.Table<CustomEntry>()
            .Where(e => e.PetId == petId && e.Date == day && e.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(e => e.Time).ToList();
    }

    /// <summary>Every custom entry in a date range, across ALL the pet's trackers: the
    /// pending window and the vet report both read this one.</summary>
    public async Task<List<CustomEntry>> GetForRangeAsync(int petId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<CustomEntry>()
            .Where(e => e.PetId == petId && e.Date >= start && e.Date <= end && e.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>The most recent entry for one custom tracker, or null: the shape a
    /// Today stat card reads (a LAST RECORDED value, never a streak).</summary>
    public async Task<CustomEntry?> GetMostRecentAsync(int customTrackerId)
    {
        var rows = await _db.Table<CustomEntry>()
            .Where(e => e.CustomTrackerId == customTrackerId && e.IsDeleted == false)
            .OrderByDescending(e => e.Date)
            .Take(12)
            .ToListAsync();
        return rows
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Time)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether the pet has logged anything against a tracker that opted INTO the report,
    /// the gate on the export sheet's toggle.
    ///
    /// <para>Deliberately not "has any custom entry at all": a household whose only own
    /// tracker is a walk (switched off, as walks are) would otherwise be shown a toggle
    /// with an empty section behind it. The export sheet's rule is that a toggle appears
    /// only when there is something for it to include.</para>
    ///
    /// <para>One small query per opted-in tracker, capped at
    /// <see cref="CustomTracker.MaxPerPet"/> of them and run once when the sheet opens: cheaper than
    /// pulling every entry the pet ever wrote to answer a yes/no.</para></summary>
    public async Task<bool> HasReportableEntriesAsync(int petId)
    {
        foreach (var tracker in await GetAllForPetAsync(petId))
        {
            if (!tracker.IncludeInReport)
                continue;
            if (await GetMostRecentAsync(tracker.Id) is not null)
                return true;
        }
        return false;
    }
}
