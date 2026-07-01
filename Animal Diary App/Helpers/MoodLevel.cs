namespace Animal_Diary_App.Helpers;

public enum MoodLevel
{
    None = 0,
    VeryBad = 1,
    Bad = 2,
    Neutral = 3,
    Good = 4,
    Excellent = 5
}

public static class MoodLevelExtensions
{
    public static string GetDisplayName(this MoodLevel mood)
    {
        var key = mood switch
        {
            MoodLevel.Excellent => "Mood_Excellent",
            MoodLevel.Good => "Mood_Good",
            MoodLevel.Neutral => "Mood_Neutral",
            MoodLevel.Bad => "Mood_Bad",
            MoodLevel.VeryBad => "Mood_VeryBad",
            _ => "Mood_None"
        };
        return LocalizationManager.Instance.GetString(key);
    }

    public static Color GetColor(this MoodLevel mood) => mood switch
    {
        MoodLevel.Excellent => Color.FromArgb("#2D5016"),
        MoodLevel.Good => Color.FromArgb("#4CAF50"),
        MoodLevel.Neutral => Color.FromArgb("#FFC107"),
        MoodLevel.Bad => Color.FromArgb("#FF9800"),
        MoodLevel.VeryBad => Color.FromArgb("#F44336"),
        _ => Color.FromArgb("#E0E0E0")
    };

    public static string GetEmoji(this MoodLevel mood) => mood switch
    {
        MoodLevel.Excellent => "😄",
        MoodLevel.Good => "😊",
        MoodLevel.Neutral => "😐",
        MoodLevel.Bad => "😟",
        MoodLevel.VeryBad => "😢",
        _ => "❓"
    };
}
