namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// The two arrangements, and what each one has to actually reveal.
///
/// <para>These are not layout-tidiness tests. Each lens exists because it answers a
/// question a list and a calendar cannot, and if the arrangement stops answering it
/// the lens is decoration: a habit at 3am has to become a horizontal track on
/// <b>History</b> and a wedge on <b>Cycle</b>, and a twelve-day rhythm has to line up
/// when folded at twelve and stay scattered at anything else.</para>
/// </summary>
public class ConstellationLensTests
{
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 4, 1);

    private const double Width = 600;
    private const double Height = 600;

    private static CelestialEvent At(DateTime when) =>
        new(when, CelestialCategory.Seizure, "x", string.Empty);

    // ── History: across is the date, down is the time of day ─────────────────

    [Fact]
    public void History_AcrossIsTheDate()
    {
        var stars = ConstellationLayout.PlaceOnGrid(
            new[] { At(From.AddHours(9)), At(From.AddDays(40).AddHours(9)) },
            From, To, Width, Height);

        Assert.True(stars[0].X < stars[1].X, "time did not read left to right");
    }

    [Fact]
    public void History_DownIsTheTimeOfDay()
    {
        // Midnight at the top, noon in the middle, late evening at the foot.
        var stars = ConstellationLayout.PlaceOnGrid(
            new[]
            {
                At(From.AddDays(10)),
                At(From.AddDays(10).AddHours(12)),
                At(From.AddDays(10).AddHours(23)),
            },
            From, To, Width, Height);

        Assert.True(stars[0].Y < stars[1].Y);
        Assert.True(stars[1].Y < stars[2].Y);
    }

    [Fact]
    public void History_TheSameHourOnDifferentDaysDrawsAHorizontalTrack()
    {
        // The discovery this lens exists for. Twice-daily medication becomes two clean
        // lines across the sky, and anything scattered against them reads instantly.
        var events = Enumerable.Range(0, 20)
            .Select(i => At(From.AddDays(i * 4).AddHours(8)))
            .ToArray();

        var stars = ConstellationLayout.PlaceOnGrid(events, From, To, Width, Height);

        foreach (var star in stars)
            Assert.Equal(stars[0].Y, star.Y, 0);
    }

    [Fact]
    public void History_SeveralInOneDayStackVertically()
    {
        // Three seizures in a day is a vertical run at one date — not a taller bar,
        // not a bigger dot: they are simply at the times they happened.
        var day = From.AddDays(20);
        var stars = ConstellationLayout.PlaceOnGrid(
            new[] { At(day.AddHours(2)), At(day.AddHours(3)), At(day.AddHours(4)) },
            From, To, Width, Height);

        var spread = stars.Max(s => s.X) - stars.Min(s => s.X);
        Assert.True(spread < 12, $"one day's entries spread {spread:0} across the date axis");
        Assert.True(stars[0].Y < stars[2].Y);
    }

    [Fact]
    public void History_EveryStarStaysInsideTheCard()
    {
        var events = Enumerable.Range(0, 400).Select(i => At(From.AddHours(i * 5))).ToArray();

        foreach (var star in ConstellationLayout.PlaceOnGrid(events, From, To, Width, Height))
        {
            Assert.InRange(star.X, -3, Width + 3);
            Assert.InRange(star.Y, 0, Height);
        }
    }

    [Fact]
    public void History_EmptyOrUnsizedCanvas_PlacesNothing()
    {
        Assert.Empty(ConstellationLayout.PlaceOnGrid(Array.Empty<CelestialEvent>(), From, To, Width, Height));
        Assert.Empty(ConstellationLayout.PlaceOnGrid(new[] { At(From) }, From, To, 0, Height));
        Assert.Empty(ConstellationLayout.PlaceOnGrid(new[] { At(From) }, From, To, Width, 0));
    }

    // ── Cycle: around is the fold, out is how far through the stretch ─────────

    [Fact]
    public void Cycle_AtADayItIsAClockFace()
    {
        // Midnight up, six on the right, noon down: a clock, or nobody can read a wedge
        // off it without being taught the picture first.
        var midnight = ConstellationLayout.PlaceOnRing(new[] { At(To.AddDays(-1)) }, From, To, 1, Width, Height)[0];
        var sixAm = ConstellationLayout.PlaceOnRing(new[] { At(To.AddDays(-1).AddHours(6)) }, From, To, 1, Width, Height)[0];
        var noon = ConstellationLayout.PlaceOnRing(new[] { At(To.AddDays(-1).AddHours(12)) }, From, To, 1, Width, Height)[0];

        Assert.True(midnight.Y < Height / 2, "midnight was not at the top");
        Assert.True(sixAm.X > Width / 2, "06:00 was not on the right");
        Assert.True(noon.Y > Height / 2, "noon was not at the bottom");
    }

    [Fact]
    public void Cycle_AMatchingPeriodLinesUpAsASpoke()
    {
        var events = Enumerable.Range(0, 8)
            .Select(i => At(From.AddDays(i * 12).AddHours(4)))
            .ToArray();

        var bearings = Bearings(ConstellationLayout.PlaceOnRing(events, From, To, 12, Width, Height));

        foreach (var bearing in bearings)
            Assert.Equal(bearings[0], bearing, 1);
    }

    [Fact]
    public void Cycle_AMismatchedPeriodStaysScattered()
    {
        // The honest other half: a lens that made everything look meaningful would be
        // worse than no lens.
        var events = Enumerable.Range(0, 8)
            .Select(i => At(From.AddDays(i * 12).AddHours(4)))
            .ToArray();

        var bearings = Bearings(ConstellationLayout.PlaceOnRing(events, From, To, 7, Width, Height));
        var spread = bearings.Max() - bearings.Min();

        Assert.True(spread > 1.5, $"a mismatched fold still huddled inside {spread:0.0} radians");
    }

    [Fact]
    public void Cycle_HasNoSeam()
    {
        // The reason the fold is a ring and not a line. Two entries either side of the
        // fold boundary are minutes apart in cycle terms; on a line they land at
        // opposite edges and read as two unrelated clumps, so a genuine rhythm could be
        // hidden purely by where the range began.
        const double period = 10;
        var stars = ConstellationLayout.PlaceOnRing(
            new[] { At(From.AddDays(period).AddMinutes(-6)), At(From.AddDays(period).AddMinutes(6)) },
            From, To, period, Width, Height);

        var b = Bearings(stars);
        var apart = Math.Abs(Math.Atan2(Math.Sin(b[0] - b[1]), Math.Cos(b[0] - b[1])));

        Assert.True(apart < 0.1, $"twelve minutes apart landed {apart:0.00} radians apart");
    }

    [Fact]
    public void Cycle_OlderSitsInnerAndNewerSitsOuter()
    {
        // The radius means something, so it has to hold: the drawing labels it and the
        // caption says which way it runs.
        var stars = ConstellationLayout.PlaceOnRing(
            new[] { At(From.AddHours(9)), At(To.AddDays(-1).AddHours(9)) },
            From, To, 1, Width, Height);

        Assert.True(Radius(stars[0]) < Radius(stars[1]));
    }

    [Fact]
    public void Cycle_LeavesTheMiddleEmpty()
    {
        // Everything from the first day would otherwise pile onto one point, making the
        // oldest entries the least legible.
        var events = Enumerable.Range(0, 24).Select(i => At(From.AddHours(i))).ToArray();

        foreach (var star in ConstellationLayout.PlaceOnRing(events, From, To, 1, Width, Height))
            Assert.True(Radius(star) > 30, $"a star landed {Radius(star):0} from the centre");
    }

    [Fact]
    public void Cycle_IgnoresANonsensePeriodRatherThanDividingByZero()
    {
        Assert.Empty(ConstellationLayout.PlaceOnRing(new[] { At(From) }, From, To, 0, Width, Height));
        Assert.Empty(ConstellationLayout.PlaceOnRing(new[] { At(From) }, From, To, -3, Width, Height));
    }

    [Fact]
    public void Fold_CannotBeSetLongerThanHalfTheStretch()
    {
        // You cannot see a repeat in a window that does not hold two of them. A bound on
        // what the picture can SHOW — never a hint about what the answer is.
        Assert.Equal(3, MaxFold(7));
        Assert.Equal(15, MaxFold(30));
        Assert.Equal(45, MaxFold(90));
        Assert.Equal(60, MaxFold(365));

        static double MaxFold(int rangeDays) => Math.Max(2, Math.Min(60, rangeDays / 2));
    }

    // ── Neither lens touches the records ─────────────────────────────────────

    [Fact]
    public void BothLensesPlaceTheSameEventsOneForOne()
    {
        // Index is identity everywhere — the detail sheet, the hit test and the drawing
        // all key off it, so a lens that dropped or reordered a star would describe the
        // wrong entry when one was tapped.
        var events = Enumerable.Range(0, 40).Select(i => At(From.AddHours(i * 13))).ToArray();

        Assert.Equal(events.Length,
            ConstellationLayout.PlaceOnGrid(events, From, To, Width, Height).Length);
        Assert.Equal(events.Length,
            ConstellationLayout.PlaceOnRing(events, From, To, 14, Width, Height).Length);
    }

    [Fact]
    public void StarSizeIsTheSameForEveryKind()
    {
        // Sizing a seizure differently from a dose would be the app ranking them. What
        // varies is only how much room there is, and only inside a tight band.
        var sparse = ConstellationLayout.StarRadius(Width, 5);
        var dense = ConstellationLayout.StarRadius(Width, 500);

        Assert.True(dense < sparse);
        Assert.InRange(dense, 3.3, 5.0);
        Assert.InRange(sparse, 3.3, 5.0);
        Assert.True(ConstellationLayout.StarRadius(Width * 8, 400) > ConstellationLayout.StarRadius(Width, 400));
    }

    // ── Arriving ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheSkyWritesItselfLeftToRight()
    {
        // The one ornament that teaches something: the stagger runs along the DATE
        // axis, so watching the load explains the horizontal axis before the caption
        // has been read. Early in the reveal the oldest entries have started and the
        // newest have not.
        var early = ConstellationLayout.ArrivalOf(0.2, xFraction: 0.0);
        var late = ConstellationLayout.ArrivalOf(0.2, xFraction: 1.0);

        Assert.True(early > 0, "the oldest entry had not begun arriving");
        Assert.Equal(0, late);
    }

    [Fact]
    public void EveryStarHasArrivedByTheEnd()
    {
        // Whatever the stagger does in the middle, nothing may be left faded out: the
        // resting picture is the whole history, not most of it.
        foreach (var fraction in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(1, ConstellationLayout.ArrivalOf(1, fraction));
            Assert.InRange(ConstellationLayout.ArrivalOf(0.5, fraction), 0, 1);
        }
    }

    private static double[] Bearings(IReadOnlyList<SkyStar> stars) => stars
        .Select(s => Math.Atan2(s.Y - Height / 2, s.X - Width / 2))
        .ToArray();

    private static double Radius(SkyStar s) =>
        Math.Sqrt(Math.Pow(s.X - Width / 2, 2) + Math.Pow(s.Y - Height / 2, 2));
}
