using Animal_Diary_App.Data.Services.Notifications;
using Xunit;

namespace Animal_Diary_App.Tests;

/// <summary>
/// The expansion of one weekday+time recurrence rule into concrete occurrences.
///
/// <para>This is the smallest and most consequential function in the reminder path:
/// both the scheduler (what to arm) and the dose reconciler (what was missed) read it,
/// so an off-by-one week here is a dose that never fires or a false "missed dose".
/// The window is deliberately half-open — <c>(from, until]</c> — because the scheduler
/// re-runs on every launch and an inclusive lower bound would re-arm an occurrence it
/// had just resolved.</para>
/// </summary>
public class MedicationScheduleExpanderTests
{
    // A fixed Monday, so every case reads as an explicit weekday offset.
    private static readonly DateTime Monday = new(2026, 7, 6);

    private static List<DateTime> Expand(DayOfWeek day, TimeSpan time, DateTime from, DateTime until)
        => MedicationScheduleExpander.Expand(day, time, from, until).ToList();

    [Fact]
    public void SameDayLaterTime_IsIncluded()
    {
        var from = Monday.AddHours(8);
        var result = Expand(DayOfWeek.Monday, TimeSpan.FromHours(18), from, Monday.AddDays(7));

        Assert.Equal(Monday.AddHours(18), result[0]);
    }

    [Fact]
    public void SameDayEarlierTime_SkipsToNextWeek()
    {
        // 08:00 has already passed at 09:00, so today's occurrence is behind us.
        var from = Monday.AddHours(9);
        var result = Expand(DayOfWeek.Monday, TimeSpan.FromHours(8), from, Monday.AddDays(14));

        Assert.Equal(Monday.AddDays(7).AddHours(8), result[0]);
    }

    [Fact]
    public void ExactBoundary_IsExclusiveAtFromAndInclusiveAtUntil()
    {
        var at8 = Monday.AddHours(8);

        // from == the occurrence: excluded, or a re-run re-arms what it just resolved.
        var fromBoundary = Expand(DayOfWeek.Monday, TimeSpan.FromHours(8), at8, Monday.AddDays(8));
        Assert.Equal(at8.AddDays(7), fromBoundary[0]);

        // until == the occurrence: included, so a horizon ending exactly on a dose
        // still arms it.
        var untilBoundary = Expand(DayOfWeek.Monday, TimeSpan.FromHours(8), Monday.AddDays(-1), at8);
        Assert.Equal(new[] { at8 }, untilBoundary);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 0)]
    [InlineData(DayOfWeek.Tuesday, 1)]
    [InlineData(DayOfWeek.Wednesday, 2)]
    [InlineData(DayOfWeek.Thursday, 3)]
    [InlineData(DayOfWeek.Friday, 4)]
    [InlineData(DayOfWeek.Saturday, 5)]
    [InlineData(DayOfWeek.Sunday, 6)]
    public void FirstOccurrence_LandsOnTheRequestedWeekday(DayOfWeek day, int expectedOffset)
    {
        var result = Expand(day, TimeSpan.FromHours(9), Monday, Monday.AddDays(7));

        Assert.Equal(Monday.AddDays(expectedOffset).AddHours(9), result[0]);
        Assert.Equal(day, result[0].DayOfWeek);
    }

    [Fact]
    public void SundayFromSunday_DoesNotWrapBackwards()
    {
        // Sunday is day 0 in .NET but the app's weeks start on Monday; the modulo in
        // Expand is where an inverted offset would send this to the previous week.
        var sunday = Monday.AddDays(6);
        var result = Expand(DayOfWeek.Sunday, TimeSpan.FromHours(9), sunday, sunday.AddDays(7));

        Assert.Equal(sunday.AddHours(9), result[0]);
    }

    [Fact]
    public void RepeatsWeekly_AcrossTheHorizon()
    {
        // 14 days is the Android horizon; a weekly rule must produce exactly two.
        var result = Expand(DayOfWeek.Wednesday, TimeSpan.FromHours(7), Monday, Monday.AddDays(14));

        Assert.Equal(2, result.Count);
        Assert.Equal(TimeSpan.FromDays(7), result[1] - result[0]);
        Assert.All(result, d => Assert.Equal(DayOfWeek.Wednesday, d.DayOfWeek));
        Assert.All(result, d => Assert.Equal(TimeSpan.FromHours(7), d.TimeOfDay));
    }

    [Fact]
    public void WindowEndingBeforeTheFirstOccurrence_YieldsNothing()
    {
        var result = Expand(DayOfWeek.Friday, TimeSpan.FromHours(9), Monday, Monday.AddDays(2));

        Assert.Empty(result);
    }

    [Fact]
    public void UntilBeforeFrom_YieldsNothing()
    {
        // A degenerate window (a horizon already behind the clock) must terminate, not
        // spin: the loop's guard is the only thing standing between this and forever.
        var result = Expand(DayOfWeek.Monday, TimeSpan.FromHours(9), Monday, Monday.AddDays(-5));

        Assert.Empty(result);
    }

    [Fact]
    public void OccurrencesAreWallClock_SoADstShiftKeepsTheSameLocalTime()
    {
        // Europe/Berlin springs forward on 2026-03-29. Expansion is deliberately in
        // local wall-clock time — 08:00 stays 08:00 across the boundary, and the
        // conversion to UTC happens later, at the future instant. If this ever returned
        // 07:00 or 09:00, every dose after a DST change would drift by an hour.
        var beforeDst = new DateTime(2026, 3, 23);   // the Monday before
        var result = Expand(DayOfWeek.Monday, TimeSpan.FromHours(8), beforeDst, beforeDst.AddDays(14));

        Assert.All(result, d => Assert.Equal(TimeSpan.FromHours(8), d.TimeOfDay));
    }
}
