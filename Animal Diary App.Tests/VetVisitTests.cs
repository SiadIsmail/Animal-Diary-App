namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Notifications;
using Xunit;

/// <summary>
/// The vet visit's derived state, and the id its one reminder is armed under.
///
/// <para>Both fail silently. A visit that turns "past" at lunchtime takes the summary
/// off the screen while the owner is still in the waiting room; a notification id that
/// strays into a neighbouring range cancels somebody else's reminder, and nothing
/// anywhere reports it.</para>
/// </summary>
public class VetVisitTests
{
    private static VetVisit Visit(DateTime date, TimeSpan? time = null, string note = "") => new()
    {
        Id = 1,
        PetId = 3,
        Date = date,
        Time = time,
        VisitNote = note,
    };

    // ── Past is a DAY, not an hour ───────────────────────────────────────────

    /// <summary>A morning appointment must not read as past by lunchtime. Comparing the
    /// visit's moment against <c>DateTime.Now</c> instead of its date against today is
    /// the obvious implementation and the wrong one — it would pull the summary out from
    /// under someone who is still sitting in the waiting room.</summary>
    [Fact]
    public void AVisitEarlierToday_IsNotPastYet()
    {
        var visit = Visit(DateTime.Today, new TimeSpan(1, 0, 0));

        Assert.False(visit.IsPast);
    }

    [Fact]
    public void Today_IsNotPast_AndYesterdayIs()
    {
        Assert.False(Visit(DateTime.Today).IsPast);
        Assert.True(Visit(DateTime.Today.AddDays(-1)).IsPast);
        Assert.False(Visit(DateTime.Today.AddDays(1)).IsPast);
    }

    /// <summary>The date is stored date-only; a time that crept into it must not move
    /// the visit into another day.</summary>
    [Fact]
    public void ATimeOnTheDateColumn_DoesNotShiftTheDay()
    {
        var visit = Visit(DateTime.Today.AddHours(23));

        Assert.False(visit.IsPast);
        Assert.Equal(DateTime.Today, visit.When.Date);
    }

    // ── An unknown time is an answer ─────────────────────────────────────────

    /// <summary>No time means the owner knew the day and not the slot. It sorts at the
    /// start of its day rather than being pushed to some invented hour.</summary>
    [Fact]
    public void AVisitWithNoTime_SitsAtTheStartOfItsDay()
    {
        var visit = Visit(new DateTime(2026, 9, 3));

        Assert.Null(visit.Time);
        Assert.Equal(new DateTime(2026, 9, 3), visit.When);
    }

    [Fact]
    public void AVisitWithATime_SitsAtIt()
    {
        var visit = Visit(new DateTime(2026, 9, 3), new TimeSpan(15, 30, 0));

        Assert.Equal(new DateTime(2026, 9, 3, 15, 30, 0), visit.When);
    }

    // ── "How did it go?" is offered once, and only when it applies ───────────

    [Fact]
    public void NeedsNote_OnlyForAPastVisitWithNothingWrittenDown()
    {
        Assert.True(Visit(DateTime.Today.AddDays(-1)).NeedsNote);
        Assert.False(Visit(DateTime.Today.AddDays(-1), note: "All fine").NeedsNote);
        Assert.False(Visit(DateTime.Today.AddDays(1)).NeedsNote);
    }

    /// <summary>Whitespace is not a note. Otherwise a stray space would retire the
    /// prompt and the owner would never be asked.</summary>
    [Fact]
    public void WhitespaceIsNotANote()
        => Assert.True(Visit(DateTime.Today.AddDays(-1), note: "   ").NeedsNote);

    // ── The reminder ─────────────────────────────────────────────────────────

    /// <summary>The evening before, whatever time of day the visit is at — including a
    /// visit with no time at all, which is exactly the case a "N hours before" rule
    /// could not answer.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(7)]
    [InlineData(23)]
    public void TheReminderFires_TheEveningBefore(int? visitHour)
    {
        var date = new DateTime(2026, 9, 3);
        var visit = Visit(date, visitHour is int h ? TimeSpan.FromHours(h) : null);

        Assert.Equal(new DateTime(2026, 9, 2, 18, 0, 0), visit.ReminderAt);
    }

    // ── Notification ids ─────────────────────────────────────────────────────

    [Fact]
    public void EachVisitGetsItsOwnNotificationId()
        => Assert.NotEqual(NotificationIds.Appointment(1), NotificationIds.Appointment(2));

    /// <summary>
    /// The appointment block stays inside its own million and never reaches the next
    /// one.
    ///
    /// <para>This is the guard the id scheme exists for: every type owns a range so a
    /// cancel can never hit another type's notification. The appointment id is
    /// deliberately NOT multiplied by <c>SlotsPerEntity</c> — a visit has exactly one
    /// reminder, and reserving ten slots each would spend the range ten times faster for
    /// headroom nothing can use.</para>
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(99_999)]
    [InlineData(999_999)]
    public void AppointmentIdsStayInsideTheirOwnRange(int visitId)
    {
        var id = NotificationIds.Appointment(visitId);

        Assert.InRange(id, 4_000_001, 4_999_999);

        // And nowhere near the ranges either side of it.
        Assert.NotEqual(NotificationIds.WeightCheckIn(visitId), id);
        Assert.NotEqual(NotificationIds.DailyCare(visitId), id);
    }
}
