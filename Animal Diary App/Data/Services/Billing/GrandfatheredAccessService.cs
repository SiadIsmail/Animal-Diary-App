namespace Animal_Diary_App.Data.Services.Billing;

using System.Diagnostics;

using Animal_Diary_App.Data.Services.Data;

/// <summary>
/// The app-side <see cref="IGrandfatheredAccess"/>: a one-shot snapshot, taken on the
/// first launch after the paid boundary moved, of the two things this install already had
/// and therefore keeps — cloud backup, and the pets it was caregiving on.
///
/// <para><b>Captured once and never again.</b> <see cref="SettingsFlags.GrandfatherCaptured"/>
/// is the guard, and it is written in the same step as the values, so a second launch can
/// only ever read. Without that guard someone who turned backup on a month later would be
/// grandfathered into a paid feature by nothing more than a relaunch.</para>
///
/// <para>Reads are synchronous and allocation-free because the gate calls them on the UI
/// thread on every paid surface: <see cref="CaptureOrLoadAsync"/> pulls the two values into
/// memory once at startup and everything after that is a field read. Before it runs,
/// nothing is grandfathered — which is the safe direction, since the gate is optimistic
/// while the entitlement is still unknown anyway.</para>
///
/// <para>Device-scoped, in <c>AppSettings</c> like every other preference, so a reinstall
/// loses it. That is accepted: the affected population is tiny, and the failure mode is
/// being asked to subscribe, never losing data. Rebuilding it account-side would mean a new
/// server column and a migration for a one-time, shrinking population.</para>
/// </summary>
public sealed class GrandfatheredAccessService : IGrandfatheredAccess
{
    /// <summary>Pets this device was caregiving on when the boundary moved, stored as one
    /// newline-joined settings value. A pet SyncId is a uuid, so it can never contain the
    /// separator.</summary>
    private const string CaregiverPetsKey = "GrandfatheredCaregiverPets";
    private const string BackupKey = "GrandfatheredBackup";
    private const char Separator = '\n';

    private readonly SettingsService _settings;

    private volatile HashSet<string> _pets = new(StringComparer.Ordinal);
    private volatile bool _backup;

    public GrandfatheredAccessService(SettingsService settings) => _settings = settings;

    public bool BackupIncluded => _backup;

    public bool SponsorshipIncluded(string? petSyncId)
        => !string.IsNullOrEmpty(petSyncId) && _pets.Contains(petSyncId);

    /// <summary>Take the snapshot if this is the first launch since the boundary moved;
    /// otherwise just load what was taken then. Idempotent and non-throwing — a failure
    /// here means "nothing grandfathered", which is a subscribe prompt rather than a
    /// broken app.</summary>
    /// <param name="backupEnabledNow">Whether cloud backup is on for this install right now.</param>
    /// <param name="caregiverPetSyncIds">The pets this install is currently a caregiver on.
    /// Read AFTER the sync engine has loaded its cached membership map, or the snapshot is
    /// empty and a real caregiver is grandfathered out of what they had.</param>
    public async Task CaptureOrLoadAsync(bool backupEnabledNow, IReadOnlyCollection<string> caregiverPetSyncIds)
    {
        try
        {
            if (await _settings.GetFlagAsync(SettingsFlags.GrandfatherCaptured))
            {
                _backup = await _settings.GetFlagAsync(BackupKey);
                var stored = await _settings.GetValueAsync(CaregiverPetsKey);
                _pets = stored is null
                    ? new HashSet<string>(StringComparer.Ordinal)
                    : new HashSet<string>(
                        stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                return;
            }

            var pets = new HashSet<string>(caregiverPetSyncIds.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);

            await _settings.SetFlagAsync(BackupKey, backupEnabledNow);
            await _settings.SetValueAsync(CaregiverPetsKey, string.Join(Separator, pets));
            // Last, so a crash midway re-runs the capture rather than locking in a half-written one.
            await _settings.SetFlagAsync(SettingsFlags.GrandfatherCaptured, true);

            _backup = backupEnabledNow;
            _pets = pets;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] grandfather capture failed: {ex.Message}");
        }
    }
}
