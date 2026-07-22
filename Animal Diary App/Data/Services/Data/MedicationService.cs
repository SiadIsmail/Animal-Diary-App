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
        await _db.InsertAsync(SyncStamp.Touch(medication));
    }

    public async Task UpdateMedicationAsync(Medication medication)
    {
        await _db.UpdateAsync(SyncStamp.Touch(medication));
    }

    /// <summary>Delete a medication together with all of its schedule rows.
    /// Soft deletes — the rows become tombstones so the deletion can sync.</summary>
    public async Task DeleteMedicationAsync(int medicationId)
    {
        await DeleteSchedulesForMedicationAsync(medicationId);
        var med = await GetMedicationByIdAsync(medicationId);
        if (med != null)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(med));
    }

    public async Task<Medication?> GetMedicationByIdAsync(int id)
    {
        return await _db.Table<Medication>()
            .Where(m => m.Id == id && m.IsDeleted == false)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Medication>> GetMedicationsByPetIdAsync(int id)
    {
        return await _db.Table<Medication>()
            .Where(m => m.PetId == id && m.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>All medications across every pet (used by the global reminder refresh).</summary>
    public async Task<List<Medication>> GetAllMedicationsAsync()
    {
        return await _db.Table<Medication>()
            .Where(m => m.IsDeleted == false)
            .ToListAsync();
    }
    public async Task<List<MedicationSchedule>> GetMedicationSchedulesByMedicationIdAsync(int id)
    {
        return await _db.Table<MedicationSchedule>()
            .Where(s => s.MedicationId == id && s.IsDeleted == false)
            .ToListAsync();
    }

    /// <summary>All schedule rows for a set of medications in one query (IN clause).
    /// Avoids the per-medication round-trip when building a day/week of doses.</summary>
    public async Task<List<MedicationSchedule>> GetSchedulesForMedicationsAsync(IReadOnlyCollection<int> medicationIds)
    {
        if (medicationIds.Count == 0)
            return new List<MedicationSchedule>();

        return await _db.Table<MedicationSchedule>()
            .Where(s => medicationIds.Contains(s.MedicationId) && s.IsDeleted == false)
            .ToListAsync();
    }

    public async Task SaveMedicationScheduleAsync(MedicationSchedule schedule)
    {
        await _db.InsertAsync(SyncStamp.Touch(schedule));
    }

    /// <summary>
    /// Persist a medication together with its COMPLETE schedule set in one
    /// transaction: insert/update the medication, drop its old schedule rows, and
    /// insert the new ones. Atomic on purpose — process death between "delete old
    /// schedules" and "insert new ones" would otherwise leave a medication with no
    /// rules, silently killing its reminders on the next sync.
    /// </summary>
    public Task SaveMedicationWithSchedulesAsync(Medication medication, IReadOnlyList<MedicationSchedule> schedules)
        => _db.RunInTransactionAsync(conn =>
        {
            SyncStamp.Touch(medication);
            if (medication.Id == 0)
                conn.Insert(medication);            // assigns Id
            else
                conn.Update(medication);

            // Replace-set, sync-aware: the old rows may already exist in the cloud,
            // so they become tombstones rather than vanishing…
            var old = conn.Table<MedicationSchedule>()
                .Where(s => s.MedicationId == medication.Id && s.IsDeleted == false)
                .ToList();
            foreach (var s in old)
                conn.Update(SyncStamp.MarkDeleted(s));

            // …and the new set is always inserted as FRESH rows. Id/SyncId are
            // reset because a caller-reused row would otherwise collide with its
            // own tombstone (same local Id) or share its global identity.
            foreach (var schedule in schedules)
            {
                schedule.MedicationId = medication.Id;
                schedule.Id = 0;
                schedule.SyncId = string.Empty;
                schedule.IsDeleted = false;
                conn.Insert(SyncStamp.Touch(schedule));
            }
        });

    /// <summary>Soft-delete every schedule row for a medication (used before re-saving an edit, or on delete).</summary>
    public async Task DeleteSchedulesForMedicationAsync(int medicationId)
    {
        var rows = await _db.Table<MedicationSchedule>()
            .Where(s => s.MedicationId == medicationId && s.IsDeleted == false)
            .ToListAsync();
        foreach (var row in rows)
            await _db.UpdateAsync(SyncStamp.MarkDeleted(row));
    }

}
