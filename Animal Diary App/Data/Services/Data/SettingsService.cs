namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

public class SettingsService : Animal_Diary_App.Data.Services.Billing.ITrialStore
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

    // ── Trial + monetization state ────────────────────────────────────────────
    // The app-side free trial's start instant, and a set of one-shot UI flags (each
    // "have we shown X once?"). Stored in the same key/value AppSettings table as
    // language, so they are wiped by AppResetService like everything else.

    private const string TrialStartKey = "TrialStartUtc";

    /// <summary>The UTC instant the trial began, or null if it has not started.
    /// Persisted as ticks so it round-trips exactly.</summary>
    public async Task<DateTime?> GetTrialStartUtcAsync()
    {
        try
        {
            var setting = await _db.FindAsync<AppSettings>(TrialStartKey);
            if (setting != null && long.TryParse(setting.Value, out var ticks))
                return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error retrieving trial start: {ex.Message}");
        }
        return null;
    }

    public async Task SetTrialStartUtcAsync(DateTime startUtc)
        => await UpsertAsync(TrialStartKey, startUtc.ToUniversalTime().Ticks.ToString());

    /// <summary>Read a one-shot flag (default false). Keys are the <c>SettingsFlags.*</c>
    /// constants — e.g. "the trial explainer has been shown".</summary>
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

/// <summary>One-shot UI flag keys for the trial/subscription funnel — each answers
/// "have we already shown this once?". Constants so producers and readers agree.</summary>
public static class SettingsFlags
{
    /// <summary>The user completed their first real log (dose given or journal entry).</summary>
    public const string FirstLogDone = "FirstLogDone";
    /// <summary>The post-first-log trial explainer has been shown.</summary>
    public const string TrialExplainerShown = "TrialExplainerShown";
    /// <summary>The single pre-trial-end nudge has been shown.</summary>
    public const string PreEndNudgeShown = "PreEndNudgeShown";
    /// <summary>The reassurance shown once when the app first enters the read-only state.</summary>
    public const string ReadOnlyReassuranceShown = "ReadOnlyReassuranceShown";
}