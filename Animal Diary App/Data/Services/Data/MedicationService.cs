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

    /// <summary>
    /// Write a medication back, appending whatever the treatment ledger sees change.
    ///
    /// <para>The stored row is read on the SAME connection, inside the transaction and
    /// BEFORE the update — the caller hands us an already-mutated object, so the
    /// database is the only remaining witness to what it used to say. Schedules are not
    /// this path's business (both sides get the empty set), so it can never claim a
    /// schedule change it did not make; the archive/restore flip is what actually
    /// travels through here.</para>
    /// </summary>
    public Task UpdateMedicationAsync(Medication medication)
        => _db.RunInTransactionAsync(conn =>
        {
            var before = conn.Table<Medication>()
                .Where(m => m.Id == medication.Id)
                .FirstOrDefault();

            conn.Update(SyncStamp.Touch(medication));

            if (before == null)
                return;

            AppendLedger(conn, MedicationLedger.Diff(
                before, NoSchedules, medication, NoSchedules,
                DateTime.UtcNow, MedicationScheduleText.Describe));
        });

    /// <summary>Delete a medication together with all of its schedule rows.
    /// Soft deletes — the rows become tombstones so the deletion can sync.</summary>
    public async Task DeleteMedicationAsync(int medicationId)
    {
        await DeleteSchedulesForMedicationAsync(medicationId);
        var med = await GetMedicationByIdAsync(medicationId);
        if (med == null)
            return;

        // The ledger row is written first and outlives the medication on purpose: the
        // fact that this treatment was ever given is not deleted along with the thing
        // that gave it. It carries its own name and dose text, so it stays readable
        // with nothing left to join to.
        await _db.InsertAsync(SyncStamp.Touch(MedicationLedger.Stopped(med, DateTime.UtcNow)));
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

    /// <summary>Does any pet on this device have a live medication? A count, not a load:
    /// the Today page asks this on every single appearance to decide whether the
    /// "reminders are switched off" banner is even relevant, and it does not need the
    /// rows.</summary>
    public async Task<bool> AnyActiveMedicationsAsync()
    {
        return await _db.Table<Medication>()
            .Where(m => m.IsDeleted == false && m.IsArchived == false)
            .CountAsync() > 0;
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
            // Read the stored row BEFORE the update, on this same connection. The
            // caller mutates the Medication it loaded, so once conn.Update runs there
            // is nothing left anywhere that remembers the old dose — which is exactly
            // the history this ledger exists to stop destroying. Null = a create.
            var before = medication.Id == 0
                ? null
                : conn.Table<Medication>().Where(m => m.Id == medication.Id).FirstOrDefault();

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

            // Same transaction, deliberately: a torn write that kept the new dose and
            // lost the row recording it would be worse than no ledger at all. `old` is
            // the schedule set as it stood — it was loaded above to be tombstoned, and
            // it is the only "before" the diff needs.
            AppendLedger(conn, MedicationLedger.Diff(
                before, old, medication, schedules,
                DateTime.UtcNow, MedicationScheduleText.Describe));
        });

    /// <summary>Both ledger-writing paths append the same way — through SyncStamp, so
    /// the rows reach the cloud like any other, and one at a time because there are at
    /// most a handful per save.</summary>
    private static void AppendLedger(SQLiteConnection conn, IReadOnlyList<MedicationChange> changes)
    {
        foreach (var change in changes)
            conn.Insert(SyncStamp.Touch(change));
    }

    /// <summary>The empty schedule set, for a write path that does not touch schedules.
    /// Passing the same set as both sides is what makes "this path can never report a
    /// schedule change" structural rather than a comment.</summary>
    private static readonly IReadOnlyList<MedicationSchedule> NoSchedules = Array.Empty<MedicationSchedule>();

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
