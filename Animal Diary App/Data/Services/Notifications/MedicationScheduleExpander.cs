namespace Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// Shared expansion of a recurring weekday+time schedule rule into concrete
/// local wall-clock occurrences. Used by both the reminder scheduler (to
/// materialize future notifications) and the dose reconciler (to find past
/// scheduled doses that were never logged).
/// </summary>
public static class MedicationScheduleExpander
{
    /// <summary>
    /// Every occurrence of <paramref name="day"/> at <paramref name="time"/> in
    /// the window (<paramref name="from"/>, <paramref name="until"/>].
    /// </summary>
    public static IEnumerable<DateTime> Expand(DayOfWeek day, TimeSpan time, DateTime from, DateTime until)
    {
        var daysUntil = ((int)day - (int)from.DayOfWeek + 7) % 7;
        var candidate = from.Date.AddDays(daysUntil).Add(time);
        if (candidate <= from)
            candidate = candidate.AddDays(7);

        while (candidate <= until)
        {
            yield return candidate;
            candidate = candidate.AddDays(7);
        }
    }
}
