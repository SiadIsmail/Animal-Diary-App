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
    public async Task<List<Medication>> GetMedicationsByPetIdAsync(int id)
    {
        return await _db.Table<Medication>()
            .Where(m => m.PetId == id)
            .ToListAsync();
    }
    public async Task<List<MedicationSchedule>> GetMedicationSchedulesByMedicationIdAsync(int id)
    {
        return await _db.Table<MedicationSchedule>()
            .Where(s => s.MedicationId == id)
            .ToListAsync();
    }

    public async Task SaveMedicationScheduleAsync(MedicationSchedule schedule)
    {
        await _db.InsertAsync(schedule);
    }

    /*public async Task<List<MedicationLog>> GetMedicationLogsAsync()
    {
        return await _db.Table<MedicationLog>()
            .OrderByDescending(m => m.TakenAt)
            .ToListAsync();
    }*/
}