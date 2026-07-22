namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Repository for <see cref="MedicationDoseLog"/> adherence records. A dose is
/// keyed by (medication, date, time); setting a status is an upsert on that key,
/// clearing it soft-deletes the row (the "undo" path). A cleared key's tombstone
/// is revived by the next SetStatus for the same key, so one dose can never map
/// to two rows — the cloud keys dose logs by (medication, date, time).
/// </summary>
public class MedicationDoseLogService
{
    private readonly SQLiteAsyncConnection _db;

    public MedicationDoseLogService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>All dose logs for a pet on a given day.</summary>
    public Task<List<MedicationDoseLog>> GetByPetAndDateAsync(int petId, DateTime date)
    {
        var day = date.Date;
        return _db.Table<MedicationDoseLog>()
            .Where(l => l.PetId == petId && l.ScheduledDate == day && l.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>All dose logs for a medication within an inclusive date range.</summary>
    public Task<List<MedicationDoseLog>> GetByMedicationAndRangeAsync(int medicationId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return _db.Table<MedicationDoseLog>()
            .Where(l => l.MedicationId == medicationId && l.ScheduledDate >= start && l.ScheduledDate <= end && l.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>All dose logs for a set of medications within an inclusive date
    /// range, in one query. Callers group the result by medication id.</summary>
    public async Task<List<MedicationDoseLog>> GetByMedicationsAndRangeAsync(IReadOnlyCollection<int> medicationIds, DateTime startDate, DateTime endDate)
    {
        if (medicationIds.Count == 0)
            return new List<MedicationDoseLog>();

        var start = startDate.Date;
        var end = endDate.Date;
        return await _db.Table<MedicationDoseLog>()
            .Where(l => medicationIds.Contains(l.MedicationId) && l.ScheduledDate >= start && l.ScheduledDate <= end && l.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>The recorded outcome for a single dose, or null if none.</summary>
    public async Task<DoseStatus?> GetStatusAsync(int medicationId, DateTime date, TimeSpan time)
    {
        var existing = await GetByKeyAsync(medicationId, date, time);
        return existing?.Status;
    }

    /// <summary>Record (or update) the outcome for a single dose.</summary>
    public async Task SetStatusAsync(int medicationId, int petId, DateTime date, TimeSpan time, DoseStatus status)
    {
        // Include tombstones: an undone dose left one for this key, and reviving
        // it (rather than inserting a sibling) keeps the key unique across devices.
        var existing = await GetByKeyAsync(medicationId, date, time, includeDeleted: true);
        var resolvedAt = status == DoseStatus.Missed ? (DateTime?)null : DateTime.Now;

        if (existing != null)
        {
            existing.Status = status;
            existing.ResolvedAt = resolvedAt;
            existing.IsDeleted = false;
            await _db.UpdateAsync(SyncStamp.Touch(existing));
        }
        else
        {
            await _db.InsertAsync(SyncStamp.Touch(new MedicationDoseLog
            {
                MedicationId = medicationId,
                PetId = petId,
                ScheduledDate = date.Date,
                ScheduledTime = time,
                Status = status,
                ResolvedAt = resolvedAt
            }));
        }
    }

    /// <summary>Remove the outcome for a single dose (undo). Soft delete — the row
    /// stays as a tombstone so the undo can sync; SetStatus revives it.</summary>
    public async Task ClearStatusAsync(int medicationId, DateTime date, TimeSpan time)
    {
        var existing = await GetByKeyAsync(medicationId, date, time);
        if (existing != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(existing));
    }

    private async Task<MedicationDoseLog?> GetByKeyAsync(int medicationId, DateTime date, TimeSpan time, bool includeDeleted = false)
    {
        var day = date.Date;
        // Filter date + medication in SQL; match the TimeSpan in memory to avoid
        // relying on TimeSpan translation in the query provider. An active row wins
        // over a tombstone when both exist for the key.
        var rows = await _db.Table<MedicationDoseLog>()
            .Where(l => l.MedicationId == medicationId && l.ScheduledDate == day)
            .ToListAsync();
        return rows
            .Where(l => l.ScheduledTime == time && (includeDeleted || !l.IsDeleted))
            .OrderBy(l => l.IsDeleted)
            .FirstOrDefault();
    }
}
