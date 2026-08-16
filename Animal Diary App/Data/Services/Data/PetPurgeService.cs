namespace Animal_Diary_App.Data.Services;

using System.Diagnostics;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// Removing a pet from THIS device only — a hard, local delete that leaves no tombstone
/// and pushes nothing.
///
/// <para><b>This is not the ordinary delete.</b> <c>PetDeletionService</c> soft-deletes so
/// the removal reaches the cloud and the owner's other devices, which is right when someone
/// deletes their pet. This one is for rows that must disappear here and stay put everywhere
/// else, and there are exactly two callers with that need:</para>
/// <list type="bullet">
/// <item><b>Revoked access</b> — a caregiver who lost membership must not keep another
/// household's medical records, and tombstoning would try to delete the owner's pet.</item>
/// <item><b>Leaving demo mode</b> — seeded rows were never on an account, so there is
/// nothing to propagate and a tombstone would be a lie about data that never existed.</item>
/// </list>
///
/// <para>Extracted from <c>CloudSyncService</c>, where it was private: the cloud layer
/// happened to be the first caller, but "delete a pet locally" is not a cloud concept and
/// demo mode needed the identical three steps — rows, reminders, active-pet repair.</para>
/// </summary>
public sealed class PetPurgeService
{
    private readonly AppDatabase _db;
    private readonly MedicationReminderScheduler _reminders;
    private readonly ActivePetService _activePet;

    public PetPurgeService(
        AppDatabase db,
        MedicationReminderScheduler reminders,
        ActivePetService activePet)
    {
        _db = db;
        _reminders = reminders;
        _activePet = activePet;
    }

    /// <summary>Hard-delete a pet and everything that hangs off it from this device,
    /// cancel its reminders, and repair the active-pet selection.</summary>
    public async Task PurgePetAsync(Pet pet)
    {
        Debug.WriteLine($"[Purge] removing pet {pet.Id} ({(pet.IsDemo ? "demo" : pet.SyncId)}) locally");

        var meds = await _db.Connection.QueryAsync<Medication>(
            "select * from \"Medication\" where PetId = ?", pet.Id);

        // Children before parents, straight off the registry — MedicationSchedule's
        // predicate is a subquery over Medication, so it must run while those rows
        // still exist, which InDeletionOrder guarantees. Hard deletes, not tombstones:
        // this removal is local-only and must never propagate (see PetDeletionService
        // for the delete that does).
        await _db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var table in SyncedTables.InDeletionOrder)
                conn.Execute($"delete from \"{table.LocalTable}\" where {table.PetPredicate}", pet.Id);
        });

        // The med rows are gone, so the idempotent sync takes its cancel path
        // (notifications + pending instances).
        foreach (var med in meds)
        {
            try { await _reminders.SyncMedicationAsync(med.Id); }
            catch (Exception ex) { Debug.WriteLine($"[Purge] reminder cancel {med.Id} failed: {ex.Message}"); }
        }

        // Don't leave the UI pointing at a pet that no longer exists.
        if (_activePet.ActivePet?.Id == pet.Id)
        {
            var remaining = await _db.Connection.QueryAsync<Pet>(
                "select * from \"Pet\" where IsDeleted = 0 limit 1");
            if (remaining.Count > 0)
                await _activePet.LoadActivePetAsync(remaining[0].Id);
        }
    }
}
