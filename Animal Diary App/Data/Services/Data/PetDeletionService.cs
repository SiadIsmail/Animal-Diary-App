namespace Animal_Diary_App.Data.Services;

using System.Diagnostics;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Data.Services.Reports;

/// <summary>Which removal a pet needs, decided from the caller's cloud role.</summary>
public enum PetRemovalKind
{
    /// <summary>Not backed up (signed out, backup off, or the pet hasn't synced yet).
    /// The delete stays a local tombstone cascade; there is no cloud copy to reach.</summary>
    Local,

    /// <summary>The signed-in owner of a backed-up pet. The tombstones sync, so the
    /// delete propagates to the cloud and to everyone sharing the pet's care.</summary>
    OwnerBackedUp,

    /// <summary>A caregiver on a shared pet. They can't delete it for everyone — their
    /// exit is to leave; the next membership diff purges the pet from this device.</summary>
    Caregiver,
}

/// <summary>Outcome of a full pet delete, so the page can pop or send the owner
/// back to onboarding when nothing is left.</summary>
public readonly record struct PetRemovalResult(bool AnyPetsRemain);

/// <summary>
/// Removes a pet and everything hanging off it. Two shapes:
///
/// <list type="bullet">
/// <item><see cref="DeletePetAsync"/> — the owner/local delete. Every synced row is
/// <b>soft-deleted</b> (tombstoned via <see cref="SyncStamp"/>) rather than dropped,
/// so the removal reaches the cloud and other devices like any other change; the
/// pet's reminders are cancelled and its local report files (never synced) are
/// deleted outright. Mirrors <c>CloudSyncService.PurgePetAsync</c>'s cascade, but
/// tombstoning instead of hard-deleting because this delete must propagate.</item>
/// <item><see cref="LeaveSharedPetAsync"/> — a caregiver's exit. A membership call,
/// not a delete: the pet's data stays with the owner, and the next sync's membership
/// diff purges it locally.</item>
/// </list>
///
/// New feature work, so it's its own service (see AI/architecture.md); nothing else
/// depends on it, so injecting the cloud boundary here creates no cycle.
/// </summary>
public class PetDeletionService
{
    private readonly AppDatabase _db;
    private readonly MedicationReminderScheduler _reminders;
    private readonly ReportLibraryService _reports;
    private readonly ActivePetService _activePet;
    private readonly ICloudSharingService _sharing;
    private readonly ICloudSyncService _sync;
    private readonly ICloudAuthService _auth;

    private readonly PetPhotoService _photos;

    public PetDeletionService(
        AppDatabase db,
        MedicationReminderScheduler reminders,
        ReportLibraryService reports,
        ActivePetService activePet,
        ICloudSharingService sharing,
        ICloudSyncService sync,
        ICloudAuthService auth,
        PetPhotoService photos)
    {
        _db = db;
        _reminders = reminders;
        _reports = reports;
        _activePet = activePet;
        _sharing = sharing;
        _sync = sync;
        _auth = auth;
        _photos = photos;
    }

    /// <summary>Decide, from the pet's cached cloud role, which removal applies. A pet
    /// with no SyncId (or no known role yet) is treated as local — there is nothing to
    /// delete cloud-side, and a not-yet-synced owner's tombstones still push later.</summary>
    public PetRemovalKind DetermineKind(Pet pet)
    {
        var role = string.IsNullOrEmpty(pet.SyncId) ? null : _sync.GetPetRole(pet.SyncId);
        if (role == "caregiver")
            return PetRemovalKind.Caregiver;
        if (role == "owner" && _auth.IsSignedIn && _sync.IsBackupEnabled)
            return PetRemovalKind.OwnerBackedUp;
        return PetRemovalKind.Local;
    }

    /// <summary>Delete the pet and all of its data. Synced rows become tombstones so
    /// the removal syncs; reminders are cancelled and local report files deleted.
    /// Repairs the active-pet selection and reports whether any pet is left.</summary>
    public async Task<PetRemovalResult> DeletePetAsync(Pet pet)
    {
        var conn = _db.Connection;

        // Cancel each medication's reminders first (OS notifications + pending
        // instances). The med rows are still alive here so nothing dangles.
        var meds = await conn.QueryAsync<Medication>(
            "select * from \"Medication\" where PetId = ? and IsDeleted = 0", pet.Id);
        foreach (var med in meds)
        {
            try { await _reminders.CancelMedicationAsync(med.Id); }
            catch (Exception ex) { Debug.WriteLine($"[PetDelete] cancel reminders for med {med.Id} failed: {ex.Message}"); }
        }

        // Load every live row that belongs to the pet, then tombstone the lot in one
        // transaction. Soft delete keeps each row as a sync tombstone (see ISyncable);
        // stamping goes through SyncStamp so no write path forgets a sync column.
        var schedules = await conn.QueryAsync<MedicationSchedule>(
            "select s.* from \"MedicationSchedule\" s join \"Medication\" m on m.Id = s.MedicationId " +
            "where m.PetId = ? and s.IsDeleted = 0", pet.Id);
        var doseLogs = await conn.QueryAsync<MedicationDoseLog>(
            "select * from \"MedicationDoseLog\" where PetId = ? and IsDeleted = 0", pet.Id);
        var entries = await conn.QueryAsync<PetEntry>(
            "select * from \"PetEntry\" where PetId = ? and IsDeleted = 0", pet.Id);
        var trackers = await conn.QueryAsync<Tracker>(
            "select * from \"Tracker\" where PetId = ? and IsDeleted = 0", pet.Id);
        var conditions = await conn.QueryAsync<PetCondition>(
            "select * from \"PetCondition\" where PetId = ? and IsDeleted = 0", pet.Id);
        var glucose = await conn.QueryAsync<GlucoseEntry>(
            "select * from \"GlucoseEntry\" where PetId = ? and IsDeleted = 0", pet.Id);
        var appetite = await conn.QueryAsync<AppetiteEntry>(
            "select * from \"AppetiteEntry\" where PetId = ? and IsDeleted = 0", pet.Id);
        var seizures = await conn.QueryAsync<SeizureEntry>(
            "select * from \"SeizureEntry\" where PetId = ? and IsDeleted = 0", pet.Id);

        await conn.RunInTransactionAsync(txn =>
        {
            foreach (var s in schedules) txn.Update(SyncStamp.MarkDeleted(s));
            foreach (var m in meds) txn.Update(SyncStamp.MarkDeleted(m));
            foreach (var d in doseLogs) txn.Update(SyncStamp.MarkDeleted(d));
            foreach (var e in entries) txn.Update(SyncStamp.MarkDeleted(e));
            foreach (var t in trackers) txn.Update(SyncStamp.MarkDeleted(t));
            foreach (var c in conditions) txn.Update(SyncStamp.MarkDeleted(c));
            foreach (var g in glucose) txn.Update(SyncStamp.MarkDeleted(g));
            foreach (var a in appetite) txn.Update(SyncStamp.MarkDeleted(a));
            foreach (var s in seizures) txn.Update(SyncStamp.MarkDeleted(s));
            txn.Update(SyncStamp.MarkDeleted(pet));
        });

        // Report PDFs + preview PNGs are local-only (VetReportFile isn't synced), so
        // they're deleted outright rather than tombstoned.
        try
        {
            foreach (var report in await _reports.GetForPetAsync(pet.Id))
                await _reports.DeleteAsync(report);
        }
        catch (Exception ex) { Debug.WriteLine($"[PetDelete] report cleanup failed: {ex.Message}"); }

        // The profile photo is local-only (the file is never synced), so delete it
        // outright — the tombstoned row keeps the file name but the bytes are gone.
        _photos.Delete(pet.PhotoFileName);

        // Never leave the UI pointing at a pet that's gone. Switch to another pet, or
        // clear the selection when none remain (the page routes to onboarding).
        var remaining = await conn.QueryAsync<Pet>(
            "select * from \"Pet\" where IsDeleted = 0 order by Id limit 1");
        if (remaining.Count > 0)
            await _activePet.LoadActivePetAsync(remaining[0].Id);
        else
            _activePet.ActivePet = new Pet();

        // Signed-in owner: nudge the tombstones out now rather than waiting for the
        // debounce, so the delete reaches the cloud promptly.
        if (_auth.IsSignedIn && _sync.IsBackupEnabled)
            _sync.RequestSyncSoon();

        return new PetRemovalResult(AnyPetsRemain: remaining.Count > 0);
    }

    /// <summary>A caregiver leaves a shared pet: drop their membership, then sync so
    /// the membership diff purges the pet from this device. The owner keeps everything.</summary>
    public async Task LeaveSharedPetAsync(Pet pet)
    {
        var userId = _auth.UserId ?? string.Empty;
        if (!string.IsNullOrEmpty(pet.SyncId) && !string.IsNullOrEmpty(userId))
            await _sharing.RemoveMemberAsync(pet.SyncId, userId);
        await _sync.SyncNowAsync();
    }
}
