namespace Animal_Diary_App.Helpers;

/// <summary>
/// The app's one relative-day vocabulary: "today" / "yesterday" / "5 days ago" /
/// "upcoming", and the "Recorded {…}" line the Today stat cards put under a reading.
///
/// <para>Resolved per call, never cached: a singleton holding one of these strings
/// would survive a live language switch in the old language (see
/// AI/coding-standards.md). The Journal's day heading and the Today cards used to
/// spell the same four branches out separately.</para>
/// </summary>
public static class RelativeDay
{
    /// <summary>today / yesterday / N days ago / upcoming, for a date compared to now.</summary>
    public static string Phrase(DateTime date)
    {
        var loc = LocalizationManager.Instance;
        int diff = (DateTime.Now.Date - date.Date).Days;
        return diff switch
        {
            0 => loc.GetString("Journal_RelToday"),
            1 => loc.GetString("Journal_RelYesterday"),
            > 1 => loc.Format("Journal_RelDaysAgo", diff),
            _ => loc.GetString("Journal_RelUpcoming"),
        };
    }

    /// <summary>"Recorded today": the chip under a Today stat card's reading. States
    /// WHEN the reading was taken, never a verdict on what it says. Empty when nothing
    /// has been recorded.
    ///
    /// <para>A date in the future is read as today: a recorded fact can't be upcoming,
    /// and a device whose clock moved backwards shouldn't say so.</para></summary>
    public static string Recorded(DateTime? date)
    {
        if (date is null)
            return string.Empty;

        var day = date.Value.Date > DateTime.Now.Date ? DateTime.Now.Date : date.Value;
        return LocalizationManager.Instance.Format("Main_LoggedRelative", Phrase(day));
    }
}
