namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Stores which conditions a pet has. A pet can carry several at once, so each is a
/// <see cref="PetCondition"/> row. On first read for a pet with no rows yet, the
/// legacy single <see cref="Pet.ConditionId"/> is migrated into a row, so existing
/// pets keep their condition without a manual migration step.
///
/// This is the multi-condition store the pet page and the condition picker write to;
/// the Journal never reads it (it only ever sees the resulting trackers).
/// </summary>
public class PetConditionService
{
    private readonly SQLiteAsyncConnection _db;

    // Serializes the first-read legacy migration so overlapping Journal reloads can't
    // each fold Pet.ConditionId into a duplicate row.
    private readonly SemaphoreSlim _migrateLock = new(1, 1);

    public PetConditionService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>The condition ids a pet has, de-duplicated (empty ids ignored).
    /// Migrates the legacy <see cref="Pet.ConditionId"/> into a row on first access
    /// when no rows exist yet.</summary>
    public async Task<IReadOnlyList<string>> GetConditionIdsAsync(Pet? pet)
    {
        if (pet == null || pet.Id == 0)
            return System.Array.Empty<string>();

        var rows = await _db.Table<PetCondition>()
            .Where(c => c.PetId == pet.Id && c.IsDeleted == false)
            .ToListAsync();

        // First time we see this pet: fold the legacy single condition into a row so
        // the two paths agree from here on. A pet that was created as "None" simply
        // gets no rows (and no migration).
        if (rows.Count == 0 && !string.IsNullOrWhiteSpace(pet.ConditionId))
        {
            await _migrateLock.WaitAsync();
            try
            {
                // Re-check inside the lock — a concurrent reload may have migrated already.
                rows = await _db.Table<PetCondition>()
                    .Where(c => c.PetId == pet.Id && c.IsDeleted == false)
                    .ToListAsync();
                if (rows.Count == 0)
                {
                    await AddAsync(pet.Id, pet.ConditionId);
                    return new[] { pet.ConditionId };
                }
            }
            finally
            {
                _migrateLock.Release();
            }
        }

        return rows.Select(r => r.ConditionId)
                   .Where(id => !string.IsNullOrWhiteSpace(id))
                   .Distinct()
                   .ToList();
    }

    /// <summary>Add a condition to a pet (no-op if it already has it, or if empty).
    /// A previously removed condition left a tombstone; adding it back revives that
    /// row, so one (pet, condition) pair can never map to two rows — the cloud keys
    /// conditions by exactly that pair.</summary>
    public async Task AddAsync(int petId, string conditionId)
    {
        if (string.IsNullOrWhiteSpace(conditionId))
            return;

        var rows = await _db.Table<PetCondition>()
            .Where(c => c.PetId == petId && c.ConditionId == conditionId)
            .ToListAsync();
        if (rows.Any(r => !r.IsDeleted))
            return;

        var tombstone = rows.FirstOrDefault(r => r.IsDeleted);
        if (tombstone != null)
        {
            tombstone.IsDeleted = false;
            await _db.UpdateAsync(SyncStamp.Touch(tombstone));
            return;
        }

        await _db.InsertAsync(SyncStamp.Touch(
            new PetCondition { PetId = petId, ConditionId = conditionId }));
    }

    /// <summary>Remove a condition from a pet. Its trackers are handled separately by
    /// the caller (kept or turned off) — this only forgets the condition link. Soft
    /// delete — the rows become tombstones so the removal can sync.</summary>
    public async Task RemoveAsync(int petId, string conditionId)
    {
        var rows = await _db.Table<PetCondition>()
            .Where(c => c.PetId == petId && c.ConditionId == conditionId && c.IsDeleted == false)
            .ToListAsync();
        foreach (var row in rows)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }
}
