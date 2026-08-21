namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Reads and writes <see cref="VetVisit"/> rows.
///
/// <para>Mirrors the other stores here: every write goes through <c>SyncStamp</c>,
/// deletes are soft so the removal reaches the cloud, and every read filters
/// <c>IsDeleted == false</c>.</para>
///
/// <para><b>Past and upcoming are queries, not a column.</b> Every method here derives
/// them from the date against today, so nothing can drift out of step with the calendar
/// see the note on <see cref="VetVisit.IsPast"/>.</para>
/// </summary>
public class VetVisitService
{
    private readonly SQLiteAsyncConnection _db;

    public VetVisitService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>Insert a new visit (Id == 0) or update one in place. Returns its local
    /// id so a caller can re-arm or cancel exactly this visit's reminder.</summary>
    public async Task<int> SaveAsync(VetVisit visit)
    {
        visit.Date = visit.Date.Date;
        if (visit.Id == 0)
            await _db.InsertAsync(SyncStamp.Touch(visit));
        else
            await _db.UpdateAsync(SyncStamp.Touch(visit));
        return visit.Id;
    }

    public Task<VetVisit?> GetByIdAsync(int id) =>
        _db.Table<VetVisit>()
            .Where(v => v.Id == id && v.IsDeleted == false)
            .FirstOrDefaultAsync()!;

    /// <summary>Every visit for the pet, soonest first among the upcoming and newest
    /// first among the past: resolved by the callers, which want different orders. This
    /// returns them ascending by date, the one order the store can defend.</summary>
    public async Task<List<VetVisit>> GetAllAsync(int petId)
    {
        var rows = await _db.Table<VetVisit>()
            .Where(v => v.PetId == petId && v.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(v => v.When).ThenBy(v => v.Id).ToList();
    }

    /// <summary>Today or later, soonest first. The first of these is "the next visit".</summary>
    public async Task<List<VetVisit>> GetUpcomingAsync(int petId)
    {
        var today = DateTime.Today;
        var rows = await _db.Table<VetVisit>()
            .Where(v => v.PetId == petId && v.IsDeleted == false && v.Date >= today)
            .ToListAsync();
        return rows.OrderBy(v => v.When).ThenBy(v => v.Id).ToList();
    }

    /// <summary>Strictly before today, newest first: the list State A shows, and whose
    /// first element is the anchor for "since your last visit".</summary>
    public async Task<List<VetVisit>> GetPastAsync(int petId)
    {
        var today = DateTime.Today;
        var rows = await _db.Table<VetVisit>()
            .Where(v => v.PetId == petId && v.IsDeleted == false && v.Date < today)
            .ToListAsync();
        return rows.OrderByDescending(v => v.When).ThenByDescending(v => v.Id).ToList();
    }

    /// <summary>The next visit for this pet, or null. Cheap enough for Today to ask on
    /// every appearance.</summary>
    public async Task<VetVisit?> GetNextAsync(int petId) =>
        (await GetUpcomingAsync(petId)).FirstOrDefault();

    /// <summary>The most recent visit that has already happened, or null. This is the
    /// anchor the summary measures "since your last visit" from, and when it is null
    /// the summary says so rather than inventing a previous visit.</summary>
    public async Task<VetVisit?> GetMostRecentPastAsync(int petId) =>
        (await GetPastAsync(petId)).FirstOrDefault();

    /// <summary>Every pet's upcoming visits in one query: what the reminder scheduler
    /// walks. One round trip rather than one per pet.</summary>
    public async Task<List<VetVisit>> GetAllUpcomingAsync()
    {
        var today = DateTime.Today;
        var rows = await _db.Table<VetVisit>()
            .Where(v => v.IsDeleted == false && v.Date >= today)
            .ToListAsync();
        return rows.OrderBy(v => v.When).ToList();
    }

    /// <summary>Soft delete: the row becomes a tombstone so the removal can sync.</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await GetByIdAsync(id);
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    /// <summary>Bring a deleted visit back in place: the undo half of
    /// <see cref="DeleteAsync"/>. Revives the SAME row rather than inserting a sibling,
    /// so it keeps its global identity and other devices see the deletion reversed.</summary>
    public async Task RestoreAsync(int id)
    {
        var row = await _db.Table<VetVisit>()
            .Where(v => v.Id == id)
            .FirstOrDefaultAsync();
        if (row is null || !row.IsDeleted)
            return;

        row.IsDeleted = false;
        await _db.UpdateAsync(SyncStamp.Touch(row));
    }
}
