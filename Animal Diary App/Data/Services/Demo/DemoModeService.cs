namespace Animal_Diary_App.Data.Services.Demo;

using System.Diagnostics;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Data;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;
using SQLite;

/// <summary>
/// Turning demo mode on and off: seeding the two demo pets, and removing them again.
///
/// <para><b>The pets ARE the demo.</b> There is no read-model fixture and no parallel code
/// path — once the rows are in SQLite, the Constellation, the vet report, Today's cards,
/// the Journal chips and the timeline are all just the app reading its own database, and a
/// creator can log a live entry on camera like any owner. The cost of that is that invented
/// medical history is sitting in real tables, which is why <c>Pet.IsDemo</c> and its sync
/// guard came first (see PetScopeSql).</para>
///
/// <para>Deliberately NOT a compile-time switch, unlike the fixtures it replaces: a creator
/// holds a store build and cannot rebuild anything.</para>
/// </summary>
public sealed class DemoModeService
{
    /// <summary>Device-scoped, like the referral code and for the same reason — it predates
    /// any account, and it must survive a sign-out mid-shoot.</summary>
    private const string KeySeeded = "DemoSeeded";

    private readonly AppDatabase _db;
    private readonly SettingsService _settings;
    private readonly ActivePetService _activePet;
    private readonly PetPurgeService _purge;
    private readonly MedicationReminderScheduler _reminders;

    public DemoModeService(
        AppDatabase db,
        SettingsService settings,
        ActivePetService activePet,
        PetPurgeService purge,
        MedicationReminderScheduler reminders)
    {
        _db = db;
        _settings = settings;
        _activePet = activePet;
        _purge = purge;
        _reminders = reminders;
    }

    /// <summary>Raised after seeding or clearing, so open surfaces re-read. Reuses the same
    /// pattern as the cloud's RemoteChangesApplied — every page already knows how to reload
    /// itself on someone else's say-so.</summary>
    public event Action? Changed;

    /// <summary>Whether demo pets exist on this device right now.</summary>
    public Task<bool> IsSeededAsync() => _settings.GetFlagAsync(KeySeeded);

    /// <summary>The demo pets currently on the device, newest first.</summary>
    public Task<List<Pet>> GetDemoPetsAsync() =>
        _db.Connection.QueryAsync<Pet>(
            "select * from \"Pet\" where IsDemo = 1 and IsDeleted = 0 order by Id desc");

    // ── Seeding ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seed both demo pets and switch to the first of them.
    /// </summary>
    /// <returns>The pet now active, or null if nothing was seeded.</returns>
    public async Task<Pet?> SeedAsync()
    {
        // Re-seeding would double every pet rather than refresh it, and a creator who taps
        // twice should get the sky they already have, not two Kiras.
        if (await IsSeededAsync())
            return (await GetDemoPetsAsync()).LastOrDefault();

        var german = LocalizationManager.Instance.CurrentLanguage == "de";
        var today = DateTime.Today;
        var seeded = new List<Pet>();

        foreach (var profile in DemoProfile.All(german))
        {
            var pet = await SeedProfileAsync(profile, today, german);
            seeded.Add(pet);
        }

        await _settings.SetFlagAsync(KeySeeded, true);

        // Reminders are armed AFTER the rows land, and outside the transaction: the
        // scheduler reads the medication back from the database. A demo pet's reminders
        // firing is wanted, not tolerated — a creator filming a real dose notification
        // arriving is one of the shots this exists for, and PurgePetAsync cancels them
        // again on the way out.
        await ArmRemindersAsync();

        var active = seeded.FirstOrDefault();
        if (active is not null)
            await _activePet.LoadActivePetAsync(active.Id);

        Changed?.Invoke();
        return active;
    }

    private async Task<Pet> SeedProfileAsync(DemoProfile profile, DateTime today, bool german)
    {
        var history = DemoHistory.Build(profile, today, LocalizationManager.Instance.GetString);

        var pet = new Pet
        {
            Name = profile.Name,
            Type = profile.Species,
            Age = profile.AgeYears,
            BirthYear = today.Year - profile.AgeYears,
            // The legacy single-condition column, kept in step with the PetCondition rows
            // below — PetConditionService folds it into a row on first read for a pet that
            // has none, and a seeded pet that disagreed with itself would migrate a
            // duplicate.
            ConditionId = profile.ConditionIds.FirstOrDefault() ?? string.Empty,
            IsDemo = true,
        };

        // One transaction for the whole animal — several thousand rows inserted one at a
        // time is a visible freeze, and a half-seeded pet is worse than none.
        //
        // Rows are inserted RAW, never through SyncStamp: they get no SyncId (they have no
        // cloud identity and never can) and are not marked dirty (they would never push
        // anyway, and marking them would leave a permanent phantom in the "changes not yet
        // saved" count). See PetScopeSql for the guard that makes both safe.
        await _db.Connection.RunInTransactionAsync(conn =>
        {
            conn.Insert(pet);

            InsertForPet(conn, history.Conditions, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.Trackers, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.Entries, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.Glucose, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.AppetiteLevels, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.AppetiteAmounts, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.Seizures, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.WaterAmounts, pet.Id, (r, id) => r.PetId = id);
            InsertForPet(conn, history.WaterLevels, pet.Id, (r, id) => r.PetId = id);

            foreach (var med in history.Medications)
            {
                med.Medication.PetId = pet.Id;
                conn.Insert(med.Medication);

                foreach (var schedule in med.Schedules)
                    schedule.MedicationId = med.Medication.Id;
                conn.InsertAll(med.Schedules, runInTransaction: false);

                foreach (var log in med.DoseLogs)
                {
                    log.PetId = pet.Id;
                    log.MedicationId = med.Medication.Id;
                }
                conn.InsertAll(med.DoseLogs, runInTransaction: false);
            }

            foreach (var custom in history.CustomTrackers)
            {
                custom.Tracker.PetId = pet.Id;
                conn.Insert(custom.Tracker);

                foreach (var entry in custom.Entries)
                {
                    entry.PetId = pet.Id;
                    entry.CustomTrackerId = custom.Tracker.Id;
                }
                conn.InsertAll(custom.Entries, runInTransaction: false);
            }
        });

        Debug.WriteLine($"[Demo] seeded {profile.Name} — {history.RowCount} rows ({(german ? "de" : "en")})");
        return pet;
    }

    private static void InsertForPet<T>(
        SQLiteConnection conn, List<T> rows, int petId, Action<T, int> setPetId)
    {
        if (rows.Count == 0)
            return;

        foreach (var row in rows)
            setPetId(row, petId);

        // Already inside RunInTransactionAsync — a nested transaction here throws.
        conn.InsertAll(rows, runInTransaction: false);
    }

    private async Task ArmRemindersAsync()
    {
        var meds = await _db.Connection.QueryAsync<Medication>(
            "select m.* from \"Medication\" m join \"Pet\" p on p.Id = m.PetId " +
            "where p.IsDemo = 1 and m.IsDeleted = 0 and m.IsArchived = 0");

        foreach (var med in meds)
        {
            try { await _reminders.SyncMedicationAsync(med.Id); }
            catch (Exception ex) { Debug.WriteLine($"[Demo] arming reminders for {med.Id} failed: {ex.Message}"); }
        }
    }

    // ── Leaving ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remove every demo pet and everything hanging off them.
    ///
    /// <para>Reuses the same local hard delete a revoked caregiver gets: no tombstones (a
    /// deletion that propagated would be a claim about data that was never on an account),
    /// reminders cancelled, and the active pet repaired if it was one of these.</para>
    /// </summary>
    public async Task<int> ClearAsync()
    {
        var pets = await GetDemoPetsAsync();
        foreach (var pet in pets)
            await _purge.PurgePetAsync(pet);

        await _settings.SetFlagAsync(KeySeeded, false);

        // The purge repairs the active pet only when one it deleted was active, and it
        // picks the first row it finds. If it deleted EVERY pet — a creator who entered the
        // code on a fresh install and never made one of their own — nothing is active and
        // the app would land on a Today with no pet.
        if (_activePet.ActivePet is null || _activePet.ActivePet.Id == 0)
        {
            var remaining = await _db.Connection.QueryAsync<Pet>(
                "select * from \"Pet\" where IsDeleted = 0 order by Id limit 1");
            if (remaining.Count > 0)
                await _activePet.LoadActivePetAsync(remaining[0].Id);
        }

        Changed?.Invoke();
        return pets.Count;
    }
}
