namespace Animal_Diary_App.Helpers;

public enum MoodLevel
{
    None = 0,
    Unwell = 1,
    Low = 2,
    Okay = 3,
    Good = 4,
    Great = 5
}

public static class MoodLevelExtensions
{
    public static string GetDisplayName(this MoodLevel mood)
    {
        var key = mood switch
        {
            MoodLevel.Great => "Mood_Great",
            MoodLevel.Good => "Mood_Good",
            MoodLevel.Okay => "Mood_Okay",
            MoodLevel.Low => "Mood_Low",
            MoodLevel.Unwell => "Mood_Unwell",
            _ => "Mood_None"
        };
        return LocalizationManager.Instance.GetString(key);
    }

    /// <summary>The mood's swatch from <c>Colors.xaml</c>. Resolved through
    /// <see cref="AppColors"/>, which degrades to the fallback rather than throwing —
    /// the raw <c>Resources[key]</c> indexer this used to call throws
    /// <see cref="KeyNotFoundException"/> if a token is renamed, and it ran on the mood
    /// timeline where a crash is the worst possible outcome.</summary>
    public static Color GetColor(this MoodLevel mood)
    {
        var key = mood switch
        {
            MoodLevel.Great => "MGreat",
            MoodLevel.Good => "MGood",
            MoodLevel.Okay => "MOkay",
            MoodLevel.Low => "MLow",
            MoodLevel.Unwell => "MUnwell",
            _ => null
        };
        return key is null ? Colors.White : AppColors.Resolve(key, Colors.White);
    }

    public static string GetEmoji(this MoodLevel mood) => mood switch
    {
        MoodLevel.Great => "😄",
        MoodLevel.Good => "😊",
        MoodLevel.Okay => "😐",
        MoodLevel.Low => "😟",
        MoodLevel.Unwell => "😢",
        _ => "❓"
    };
}
