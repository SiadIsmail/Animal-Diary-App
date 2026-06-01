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
}