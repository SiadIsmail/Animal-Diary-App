namespace Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using SQLite;
using System.Collections.Generic;
using System.Linq;

public class MedicationService
{
    private readonly SQLiteAsyncConnection _db;

    public MedicationService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public async Task SaveMedicationAsync(Medication medication)
    {
        await _db.InsertAsync(medication);
    }

    public async Task UpdateMedicationAsync(Medication medication)
    {
        await _db.UpdateAsync(medication);
    }

    /// <summary>Delete a medication together with all of its schedule rows.</summary>
    public async Task DeleteMedicationAsync(int medicationId)
    {
        await DeleteSchedulesForMedicationAsync(medicationId);
        await _db.DeleteAsync<Medication>(medicationId);
    }

    public async Task<Medication?> GetMedicationByIdAsync(int id)
    {
        return await _db.Table<Medication>()
            .Where(m => m.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Medication>> GetMedicationsByPetIdAsync(int id)
    {
        return await _db.Table<Medication>()
            .Where(m => m.PetId == id)
            .ToListAsync();
    }

    /// <summary>All medications across every pet (used by the global reminder refresh).</summary>
    public async Task<List<Medication>> GetAllMedicationsAsync()
    {
        return await _db.Table<Medication>().ToListAsync();
    }
    public async Task<List<MedicationSchedule>> GetMedicationSchedulesByMedicationIdAsync(int id)
    {
        return await _db.Table<MedicationSchedule>()
            .Where(s => s.MedicationId == id)
            .ToListAsync();
    }

    /// <summary>All schedule rows for a set of medications in one query (IN clause).
    /// Avoids the per-medication round-trip when building a day/week of doses.</summary>
    public async Task<List<MedicationSchedule>> GetSchedulesForMedicationsAsync(IReadOnlyCollection<int> medicationIds)
    {
        if (medicationIds.Count == 0)
            return new List<MedicationSchedule>();

        return await _db.Table<MedicationSchedule>()
            .Where(s => medicationIds.Contains(s.MedicationId))
            .ToListAsync();
    }

    public async Task SaveMedicationScheduleAsync(MedicationSchedule schedule)
    {
        await _db.InsertAsync(schedule);
    }

    /// <summary>Remove every schedule row for a medication (used before re-saving an edit, or on delete).</summary>
    public async Task DeleteSchedulesForMedicationAsync(int medicationId)
    {
        await _db.Table<MedicationSchedule>()
            .DeleteAsync(s => s.MedicationId == medicationId);
    }

    /*public async Task<List<MedicationLog>> GetMedicationLogsAsync()
    {
        return await _db.Table<MedicationLog>()
            .OrderByDescending(m => m.TakenAt)
            .ToListAsync();
    }*/
}