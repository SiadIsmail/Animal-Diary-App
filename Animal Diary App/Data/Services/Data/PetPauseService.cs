namespace Animal_Diary_App.Data.Services;

using Microsoft.Maui.Storage;

/// <summary>
/// Per-device pause state (AI/app-voice.md §15, "Pause everything for {pet}").
///
/// Pausing a pet stops every reminder for it <b>on this device only</b> and keeps
/// the whole record. It is deliberately NOT synced: one carer stepping back from the
/// day-to-day (or grieving) must not silence the other carer's reminders, and neither
/// has to leave the shared pet to do it. Because it never touches data, "the record
/// stays" is automatic.
///
/// State lives in a single <see cref="Preferences"/> key holding the comma-separated
/// set of paused pet ids, so callers can both test one pet and enumerate all paused
/// pets (the schedulers need the latter to skip them when re-arming).
/// </summary>
public class PetPauseService
{
    private const string PausedIdsKey = "paused_pet_ids";

    /// <summary>True if reminders for this pet are paused on this device.</summary>
    public bool IsPaused(int petId) => Read().Contains(petId);

    /// <summary>The pet ids currently paused on this device.</summary>
    public IReadOnlySet<int> GetPausedPetIds() => Read();

    /// <summary>Pause this pet on this device. Idempotent.</summary>
    public void Pause(int petId)
    {
        var set = Read();
        if (set.Add(petId))
            Write(set);
    }

    /// <summary>Resume this pet on this device. Idempotent.</summary>
    public void Resume(int petId)
    {
        var set = Read();
        if (set.Remove(petId))
            Write(set);
    }

    /// <summary>Forget all pause state. Called by the full data reset so a fresh
    /// start can't inherit the old install's paused pets.</summary>
    public static void ClearPersistedState() => Preferences.Default.Remove(PausedIdsKey);

    private static HashSet<int> Read()
    {
        var raw = Preferences.Default.Get(PausedIdsKey, string.Empty);
        if (string.IsNullOrEmpty(raw))
            return new HashSet<int>();

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .ToHashSet();
    }

    private static void Write(HashSet<int> set)
    {
        if (set.Count == 0)
            Preferences.Default.Remove(PausedIdsKey);
        else
            Preferences.Default.Set(PausedIdsKey, string.Join(',', set));
    }
}
