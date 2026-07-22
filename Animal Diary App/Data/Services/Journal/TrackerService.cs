namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Persists a pet's care plan as <see cref="Tracker"/> rows. New pets are seeded
/// from <see cref="CarePlanCatalog"/> (the default trackers plus their conditions'
/// extras); after that the stored rows are the source of truth and the pet page
/// tunes them (frequency, target range, on/off).
/// </summary>
public class TrackerService
{
    private readonly SQLiteAsyncConnection _db;

    // Serializes the first-time seed. Several Journal reloads fire on startup, so two
    // could otherwise both see "no rows" and each seed the same pet, duplicating them.
    private readonly SemaphoreSlim _seedLock = new(1, 1);

    public TrackerService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>All trackers stored for a pet.</summary>
    public Task<List<Tracker>> GetForPetAsync(int petId) =>
        _db.Table<Tracker>()
            .Where(t => t.PetId == petId && t.IsDeleted == false)
            .ToListAsync();

    /// <summary>Insert a new tracker row (Id == 0) or update an existing one.</summary>
    public async Task SaveAsync(Tracker tracker)
    {
        if (tracker.Id == 0)
            await _db.InsertAsync(SyncStamp.Touch(tracker));
        else
            await _db.UpdateAsync(SyncStamp.Touch(tracker));
    }

    /// <summary>Delete one tracker row (its logged history in the typed entry tables
    /// is intentionally left untouched — turning a tracker off never deletes data).
    /// Soft delete — the row becomes a tombstone so the removal can sync.</summary>
    public Task DeleteAsync(Tracker tracker) => _db.UpdateAsync(SyncStamp.MarkDeleted(tracker));

    /// <summary>The pet's tracker of a given kind, or null. Filtered in memory rather
    /// than in SQL because <see cref="TrackerId"/> is a <c>[StoreAsText]</c> enum and
    /// enum-to-column comparisons don't translate reliably in sqlite-net.</summary>
    public async Task<Tracker?> GetByTrackerIdAsync(int petId, TrackerId trackerId)
    {
        var all = await GetForPetAsync(petId);
        return all.FirstOrDefault(t => t.TrackerId == trackerId);
    }

    /// <summary>Insert-or-update the single tracker of this kind for the pet, applying
    /// <paramref name="configure"/> to the row (existing row edited in place, else a
    /// new one created). This is how the condition setup sheets write their trackers.</summary>
    public async Task UpsertAsync(int petId, TrackerId trackerId, Action<Tracker> configure)
    {
        var tracker = await GetByTrackerIdAsync(petId, trackerId)
                      ?? new Tracker { PetId = petId, TrackerId = trackerId };

        configure(tracker);
        tracker.PetId = petId;
        tracker.TrackerId = trackerId;
        await SaveAsync(tracker);
    }

    /// <summary>Turn a tracker off: remove the pet's row of this kind if present.
    /// History is never touched.</summary>
    public async Task RemoveByTrackerIdAsync(int petId, TrackerId trackerId)
    {
        var existing = await GetByTrackerIdAsync(petId, trackerId);
        if (existing != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(existing));
    }

    /// <summary>
    /// Ensure a pet has its care-plan rows. If none are stored yet, seed them from the
    /// catalog for the given conditions and return the seeded rows. Idempotent: once
    /// any rows exist this returns them untouched, so a pet's tuned plan is never
    /// overwritten and later-added conditions add their trackers explicitly (they are
    /// not re-seeded here).
    /// </summary>
    public async Task<List<Tracker>> EnsureSeededAsync(int petId, IEnumerable<string> conditionIds)
    {
        var existing = await GetForPetAsync(petId);
        if (existing.Count > 0)
            return existing;

        await _seedLock.WaitAsync();
        try
        {
            // Re-check inside the lock — another reload may have seeded while we waited.
            existing = await GetForPetAsync(petId);
            if (existing.Count > 0)
                return existing;

            // Seed atomically: a torn seed would look like a tuned plan (rows exist)
            // and never be completed.
            await _db.RunInTransactionAsync(conn =>
            {
                foreach (var t in CarePlanCatalog.BuildDefaultPlan(conditionIds))
                {
                    t.PetId = petId;
                    conn.Insert(SyncStamp.Touch(t));
                }
            });

            return await GetForPetAsync(petId);
        }
        finally
        {
            _seedLock.Release();
        }
    }
}
