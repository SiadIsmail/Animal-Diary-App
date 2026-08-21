namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Reads and writes <see cref="VetQuestion"/> rows: the running list of things the
/// owner means to ask at the next visit.
///
/// <para>Mirrors the Journal entry services (<see cref="SeizureEntryService"/> is the
/// closest sibling): every write goes through <c>SyncStamp</c>, deletes are soft so the
/// removal reaches the cloud, and every read filters <c>IsDeleted == false</c>.</para>
///
/// <para>No range reads and no date filtering, unlike every entry service here: a
/// question is not something that happened on a day. It is open or it is answered, and
/// those two are the only questions anything asks of this store.</para>
/// </summary>
public class VetQuestionService
{
    private readonly SQLiteAsyncConnection _db;

    public VetQuestionService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>Write one down. Returns its local id, so the caller's undo can remove
    /// exactly the row it just created.</summary>
    public async Task<int> AddAsync(int petId, string text)
    {
        var question = new VetQuestion
        {
            PetId = petId,
            Text = text.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        await _db.InsertAsync(SyncStamp.Touch(question));
        return question.Id;
    }

    /// <summary>Still open, oldest first: the order they were thought of, which is the
    /// order they will be asked in. Never sorted by anything the app decided.</summary>
    public async Task<List<VetQuestion>> GetOpenAsync(int petId)
    {
        var rows = await _db.Table<VetQuestion>()
            .Where(q => q.PetId == petId && q.IsDeleted == false && q.AnsweredAtUtc == null)
            .ToListAsync();
        return rows.OrderBy(q => q.CreatedAtUtc).ThenBy(q => q.Id).ToList();
    }

    /// <summary>Every question for the pet, answered ones included, oldest first.</summary>
    public async Task<List<VetQuestion>> GetAllAsync(int petId)
    {
        var rows = await _db.Table<VetQuestion>()
            .Where(q => q.PetId == petId && q.IsDeleted == false)
            .ToListAsync();
        return rows.OrderBy(q => q.CreatedAtUtc).ThenBy(q => q.Id).ToList();
    }

    /// <summary>Tick it, or untick it. Both directions, because the tick is a tap and a
    /// tap can be a mis-tap, and an answer that cannot be taken back would make the
    /// owner hesitate over a control that costs nothing.</summary>
    public async Task SetAnsweredAsync(int id, bool answered)
    {
        var row = await GetByIdAsync(id);
        if (row is null)
            return;

        row.AnsweredAtUtc = answered ? DateTime.UtcNow : null;
        await _db.UpdateAsync(SyncStamp.Touch(row));
    }

    /// <summary>Soft delete (also the undo path for an add): the row becomes a
    /// tombstone so the removal can sync.</summary>
    public async Task DeleteAsync(int id)
    {
        var row = await GetByIdAsync(id);
        if (row != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

    /// <summary>Bring a deleted question back in place: the undo half of
    /// <see cref="DeleteAsync"/>. It revives the SAME row rather than inserting a
    /// sibling, so the question keeps its global identity and other devices see the
    /// deletion reversed rather than a second copy appear.</summary>
    public async Task RestoreAsync(int id)
    {
        var row = await _db.Table<VetQuestion>()
            .Where(q => q.Id == id)
            .FirstOrDefaultAsync();
        if (row is null || !row.IsDeleted)
            return;

        row.IsDeleted = false;
        await _db.UpdateAsync(SyncStamp.Touch(row));
    }

    private Task<VetQuestion?> GetByIdAsync(int id) =>
        _db.Table<VetQuestion>()
            .Where(q => q.Id == id && q.IsDeleted == false)
            .FirstOrDefaultAsync()!;
}
