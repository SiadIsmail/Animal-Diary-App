namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Repository for <see cref="MedicationDoseLog"/> adherence records. A dose is
/// keyed by (medication, date, time); setting a status is an upsert on that key,
/// clearing it deletes the row (the "undo" path).
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
            .Where(l => l.PetId == petId && l.ScheduledDate == day)
            .ToListAsync();
    }

    /// <summary>All dose logs for a medication within an inclusive date range.</summary>
    public Task<List<MedicationDoseLog>> GetByMedicationAndRangeAsync(int medicationId, DateTime startDate, DateTime endDate)
    {
        var start = startDate.Date;
        var end = endDate.Date;
        return _db.Table<MedicationDoseLog>()
            .Where(l => l.MedicationId == medicationId && l.ScheduledDate >= start && l.ScheduledDate <= end)
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
        var existing = await GetByKeyAsync(medicationId, date, time);
        var resolvedAt = status == DoseStatus.Missed ? (DateTime?)null : DateTime.Now;

        if (existing != null)
        {
            existing.Status = status;
            existing.ResolvedAt = resolvedAt;
            await _db.UpdateAsync(existing);
        }
        else
        {
            await _db.InsertAsync(new MedicationDoseLog
            {
                MedicationId = medicationId,
                PetId = petId,
                ScheduledDate = date.Date,
                ScheduledTime = time,
                Status = status,
                ResolvedAt = resolvedAt
            });
        }
    }

    /// <summary>Remove the outcome for a single dose (undo).</summary>
    public async Task ClearStatusAsync(int medicationId, DateTime date, TimeSpan time)
    {
        var existing = await GetByKeyAsync(medicationId, date, time);
        if (existing != null)
            await _db.DeleteAsync(existing);
    }

    private async Task<MedicationDoseLog?> GetByKeyAsync(int medicationId, DateTime date, TimeSpan time)
    {
        var day = date.Date;
        // Filter date + medication in SQL; match the TimeSpan in memory to avoid
        // relying on TimeSpan translation in the query provider.
        var rows = await _db.Table<MedicationDoseLog>()
            .Where(l => l.MedicationId == medicationId && l.ScheduledDate == day)
            .ToListAsync();
        return rows.FirstOrDefault(l => l.ScheduledTime == time);
    }
}
