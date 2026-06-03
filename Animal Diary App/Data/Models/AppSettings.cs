namespace Animal_Diary_App.Data.Models;

using SQLite;

public class AppSettings
{
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsFirstLaunch { get; set; } = true;
}
