namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

public class SettingsService
{
    private readonly SQLiteAsyncConnection _db;
    public SettingsService(AppDatabase database)
    {
        _db = database.Connection;
    }

    public async Task<bool> GetIsFirstLaunchAsync()
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>("IsFirstLaunch");
            if (setting != null)
            {
                return bool.TryParse(setting.Value, out var isFirstLaunch) && isFirstLaunch;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error retrieving first launch setting: {ex.Message}");
        }

        return true;
    }

    public async Task SetIsFirstLaunchAsync(bool isFirstLaunch)
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>("IsFirstLaunch");
            if (setting == null)
            {
                setting = new AppSettings { Key = "IsFirstLaunch", Value = isFirstLaunch.ToString() };
                await _db.InsertAsync(setting);
            }
            else
            {
                setting.Value = isFirstLaunch.ToString();
                await _db.UpdateAsync(setting);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving first launch setting: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the saved two-letter language code ("en" / "de"), or null when the
    /// user has not yet chosen one. A null result is what gates the first-launch
    /// language-selection screen.
    /// </summary>
    public async Task<string?> GetLanguageAsync()
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>("Language");
            return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting!.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error retrieving language setting: {ex.Message}");
            return null;
        }
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>("Language");
            if (setting == null)
            {
                setting = new AppSettings { Key = "Language", Value = languageCode };
                await _db.InsertAsync(setting);
            }
            else
            {
                setting.Value = languageCode;
                await _db.UpdateAsync(setting);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving language setting: {ex.Message}");
        }
    }

    // ── One-shot UI flags ─────────────────────────────────────────────────────
    // A set of one-shot UI flags (each
    // "have we shown X once?"). Stored in the same key/value AppSettings table as
    // language, so they are wiped by AppResetService like everything else.

    /// <summary>Read a one-shot flag (default false). Keys are the <c>SettingsFlags.*</c>
    /// constants — e.g. "the grant-ending heads-up has been shown".</summary>
    public async Task<bool> GetFlagAsync(string flagKey)
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>(flagKey);
            return setting != null && bool.TryParse(setting.Value, out var on) && on;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error retrieving flag {flagKey}: {ex.Message}");
            return false;
        }
    }

    public async Task SetFlagAsync(string flagKey, bool value)
        => await UpsertAsync(flagKey, value.ToString());

    /// <summary>Read a free-form preference value, or null when it has never been set.
    /// The same key/value store as language and the one-shot flags — device-scoped, wiped
    /// by <c>AppResetService</c>, and deliberately never synced (a display preference is
    /// not medical data, and the pet ids it can be keyed by are local).</summary>
    public async Task<string?> GetValueAsync(string key)
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>(key);
            return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting!.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error retrieving setting {key}: {ex.Message}");
            return null;
        }
    }

    public Task SetValueAsync(string key, string value) => UpsertAsync(key, value);

    /// <summary>Insert-or-update one key/value row. Shared by the setters above.</summary>
    private async Task UpsertAsync(string key, string value)
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>(key);
            if (setting == null)
                await _db.InsertAsync(new AppSettings { Key = key, Value = value });
            else
            {
                setting.Value = value;
                await _db.UpdateAsync(setting);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving setting {key}: {ex.Message}");
        }
    }
}

/// <summary>One-shot UI flag keys — each answers "have we already shown/done this once?".
/// Constants so producers and readers agree.</summary>
public static class SettingsFlags
{
    /// <summary>The user completed their first real log (dose given or journal entry).</summary>
    public const string FirstLogDone = "FirstLogDone";
    /// <summary>The reassurance shown once when a redeemed access code's grant has run out
    /// and the account is back on the free tier.</summary>
    public const string GrantEndedNoticeShown = "GrantEndedNoticeShown";
    /// <summary>The single heads-up before a redeemed access code's grant runs out.</summary>
    public const string GrantEndingNudgeShown = "GrantEndingNudgeShown";
    /// <summary>The owner has USED their one free "since your last visit" summary —
    /// read it to the end, or exported it. Not "opened the page": someone who taps in,
    /// looks confused and leaves has not had their free one, and this flag is the only
    /// thing standing between them and being asked to pay for something they never saw.
    ///
    /// <para>Device-scoped in <c>AppSettings</c> like every other preference, and that is
    /// the right home: it is a monetization preference, not medical data, and a reset
    /// already wipes it. A reinstall hands out another free summary, which is the same
    /// accepted, self-defeating abuse path the old trial anchor had — a reinstall also
    /// wipes the record the summary is assembled FROM.</para></summary>
    public const string FirstSummaryUsed = "FirstSummaryUsed";
    /// <summary>The owner has opened the Today stat-card picker at least once, so the
    /// spelled-out "tap a card to change what it shows" hint retires and the small
    /// pencil on each card carries the affordance from then on.</summary>
    public const string TodayCardsDiscovered = "TodayCardsDiscovered";
}