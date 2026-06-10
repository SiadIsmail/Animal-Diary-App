namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using SQLite;

/// <summary>
/// Repository for materialized <see cref="ReminderInstance"/> rows — the concrete
/// reminder occurrences expanded from recurring medication rules.
/// Filtering by status is done in memory by callers; the table is kept small by
/// pruning resolved (fired/missed) rows during catch-up.
/// </summary>
public class ReminderInstanceService
{
    private readonly SQLiteAsyncConnection _db;

    public ReminderInstanceService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public Task<int> InsertAsync(ReminderInstance instance) => _db.InsertAsync(instance);

    public Task<int> UpdateAsync(ReminderInstance instance) => _db.UpdateAsync(instance);

    public Task<int> DeleteAsync(ReminderInstance instance) => _db.DeleteAsync(instance);

    public Task<List<ReminderInstance>> GetAllAsync()
        => _db.Table<ReminderInstance>().ToListAsync();

    public Task<List<ReminderInstance>> GetByMedicationAsync(int medicationId)
        => _db.Table<ReminderInstance>().Where(i => i.MedicationId == medicationId).ToListAsync();

    public Task<int> DeleteAllByMedicationAsync(int medicationId)
        => _db.Table<ReminderInstance>().DeleteAsync(i => i.MedicationId == medicationId);
}
