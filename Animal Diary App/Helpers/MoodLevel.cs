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
    public static string GetDisplayName(this MoodLevel mood) => mood switch
    {
        MoodLevel.Excellent => "Excellent",
        MoodLevel.Good => "Good",
        MoodLevel.Neutral => "Neutral",
        MoodLevel.Bad => "Bad",
        MoodLevel.VeryBad => "Very Bad",
        _ => "No mood"
    };

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
