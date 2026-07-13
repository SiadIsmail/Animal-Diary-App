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

    public static Color GetColor(this MoodLevel mood) => mood switch
    {
        MoodLevel.Great => (Color)Application.Current!.Resources["MGreat"],
        MoodLevel.Good => (Color)Application.Current!.Resources["MGood"],
        MoodLevel.Okay => (Color)Application.Current!.Resources["MOkay"],
        MoodLevel.Low => (Color)Application.Current!.Resources["MLow"],
        MoodLevel.Unwell => (Color)Application.Current!.Resources["MUnwell"],
        _ => Colors.White
    };

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
