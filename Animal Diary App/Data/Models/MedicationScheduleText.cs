namespace Animal_Diary_App.Data.Models;

using Animal_Diary_App.Helpers;

/// <summary>
/// How a medication's schedule set reads as one line — "Mon, Wed · 08:00 · 20:00".
///
/// <para>Extracted from <c>MedicationViewModel</c> so the medication list and the
/// treatment ledger cannot drift: a ledger row saying the schedule changed has to be
/// legible next to the list row it describes, and two copies of this formatting is
/// how that stops being true.</para>
/// </summary>
public static class MedicationScheduleText
{
    /// <summary>
    /// The "when" tag: which days, then the times of day. Days are omitted when it's
    /// every day, because the cadence tag beside it already says "daily" — otherwise
    /// "4× a week" leaves the owner with no way to know WHICH days.
    /// </summary>
    public static string Describe(IReadOnlyCollection<DayOfWeek> days, IReadOnlyCollection<TimeSpan> times)
    {
        var loc = LocalizationManager.Instance;
        var clock = string.Join(" · ", times.Select(t => t.ToString(@"hh\:mm")));

        if (days.Count == 0 || days.Count >= 7)
            return clock;

        var names = string.Join(", ", days
            .OrderBy(d => ((int)d + 6) % 7) // Monday-first, matching the day picker
            .Select(d => loc.GetString(DayResourceKey(d))));

        return string.IsNullOrEmpty(clock) ? names : $"{names} · {clock}";
    }

    private static string DayResourceKey(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Day_Mon",
        DayOfWeek.Tuesday => "Day_Tue",
        DayOfWeek.Wednesday => "Day_Wed",
        DayOfWeek.Thursday => "Day_Thu",
        DayOfWeek.Friday => "Day_Fri",
        DayOfWeek.Saturday => "Day_Sat",
        _ => "Day_Sun",
    };
}
