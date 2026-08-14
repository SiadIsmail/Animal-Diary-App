namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// The two arrangements that exist to show something a list cannot: <b>when in the
/// day</b> a thing happens, and <b>whether it comes round again</b>.
///
/// <para>Both are still only time — that is what keeps them inside the rule. What is
/// pinned here is that they actually reveal the thing they claim to: a habit at 3am
/// has to become a wedge on the dial, and a twelve-day rhythm has to become an
/// alignment when folded at twelve days and a smear when folded at anything else. If
/// those two properties break, the lenses are decoration.</para>
/// </summary>
public class ConstellationLensTests
{
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 4, 1);
    private static readonly SkySignature Sky = SkySignature.Default;

    private const double Width = 600;
    private const double Height = 600;

    private static CelestialEvent At(DateTime when) =>
        new(when, CelestialCategory.Seizure, "x", string.Empty);

    // ── The Clock ────────────────────────────────────────────────────────────

    [Fact]
    public void Dial_PutsMidnightAtTheTopAndRunsClockwise()
    {
        // Midnight up, noon down, six in the morning to the right: a clock face, or
        // nobody can read a wedge off it without being taught the picture first.
        var midnight = ConstellationLayout.PlaceOnRing(
            new[] { At(To.AddDays(-1)) }, From, To, 1, Width, Height)[0];
        var sixAm = ConstellationLayout.PlaceOnRing(
            new[] { At(To.AddDays(-1).AddHours(6)) }, From, To, 1, Width, Height)[0];
        var noon = ConstellationLayout.PlaceOnRing(
            new[] { At(To.AddDays(-1).AddHours(12)) }, From, To, 1, Width, Height)[0];

        Assert.True(midnight.Y < Height / 2, "midnight was not at the top");
        Assert.True(sixAm.X > Width / 2, "06:00 was not on the right");
        Assert.True(noon.Y > Height / 2, "noon was not at the bottom");
    }

    [Fact]
    public void Dial_TheSameHourOnDifferentDaysLinesUpAsASpoke()
    {
        // The whole point of the lens. Six 3am seizures across three months must land
        // on one bearing from the centre, however far apart their dates are.
        var events = Enumerable.Range(0, 6)
            .Select(i => At(From.AddDays(i * 14).AddHours(3)))
            .ToArray();

        var stars = ConstellationLayout.PlaceOnRing(events, From, To, 1, Width, Height);

        var bearings = stars
            .Select(s => Math.Atan2(s.Y - Height / 2, s.X - Width / 2))
            .ToArray();

        foreach (var bearing in bearings)
            Assert.Equal(bearings[0], bearing, 1);
    }

    [Fact]
    public void Dial_OlderSitsInnerAndNewerSitsOuter()
    {
        var stars = ConstellationLayout.PlaceOnRing(
            new[] { At(From.AddHours(9)), At(To.AddDays(-1).AddHours(9)) },
            From, To, 1, Width, Height);

        var centre = new { X = Width / 2, Y = Height / 2 };
        double Radius(SkyStar s) =>
            Math.Sqrt(Math.Pow(s.X - centre.X, 2) + Math.Pow(s.Y - centre.Y, 2));

        Assert.True(Radius(stars[0]) < Radius(stars[1]));
    }

    [Fact]
    public void Dial_LeavesTheMiddleEmpty()
    {
        // Everything from the first day would otherwise pile onto one point, making the
        // oldest entries the least legible.
        var events = Enumerable.Range(0, 24).Select(i => At(From.AddHours(i))).ToArray();
        var stars = ConstellationLayout.PlaceOnRing(events, From, To, 1, Width, Height);

        foreach (var star in stars)
        {
            var radius = Math.Sqrt(
                Math.Pow(star.X - Width / 2, 2) + Math.Pow(star.Y - Height / 2, 2));
            Assert.True(radius > 30, $"a star landed {radius:0} from the centre of the dial");
        }
    }

    [Fact]
    public void Dial_StaysInsideTheCard()
    {
        var events = Enumerable.Range(0, 200)
            .Select(i => At(From.AddHours(i * 7))).ToArray();

        foreach (var star in ConstellationLayout.PlaceOnRing(events, From, To, 1, Width, Height))
        {
            Assert.InRange(star.X, 0, Width);
            Assert.InRange(star.Y, 0, Height);
        }
    }

    // ── The Rhythm ───────────────────────────────────────────────────────────

    [Fact]
    public void Fold_AMatchingPeriodLinesUp()
    {
        // Eight entries exactly twelve days apart. Folded at twelve, they are one
        // vertical alignment — which is the "oh" this lens exists for.
        var events = Enumerable.Range(0, 8)
            .Select(i => At(From.AddDays(i * 12).AddHours(4)))
            .ToArray();

        var stars = ConstellationLayout.PlaceOnRing(events, From, To, 12, Width, Height);

        // Same bearing from the centre — one spoke.
        var bearings = stars.Select(s => Math.Atan2(s.Y - Height / 2, s.X - Width / 2)).ToArray();
        foreach (var bearing in bearings)
            Assert.Equal(bearings[0], bearing, 1);
    }

    [Fact]
    public void Fold_AMismatchedPeriodStaysScattered()
    {
        // And the honest other half: fold the same entries at a period they do not
        // share and nothing lines up. A lens that made everything look meaningful
        // would be worse than no lens.
        var events = Enumerable.Range(0, 8)
            .Select(i => At(From.AddDays(i * 12).AddHours(4)))
            .ToArray();

        var stars = ConstellationLayout.PlaceOnRing(events, From, To, 7, Width, Height);

        var bearings = stars.Select(s => Math.Atan2(s.Y - Height / 2, s.X - Width / 2)).ToArray();
        var spread = bearings.Max() - bearings.Min();
        Assert.True(spread > 1.5,
            $"a mismatched fold still huddled inside {spread:0.0} radians");
    }

    [Fact]
    public void Fold_EveryStarLandsInsideOneTurn()
    {
        var events = Enumerable.Range(0, 300)
            .Select(i => At(From.AddHours(i * 5))).ToArray();

        foreach (var star in ConstellationLayout.PlaceOnRing(events, From, To, 9, Width, Height))
        {
            Assert.InRange(star.X, 0, Width);
            Assert.InRange(star.Y, 0, Height);
        }
    }

    [Fact]
    public void Fold_HasNoSeam()
    {
        // The reason the fold is a RING and not a line.
        //
        // Two entries either side of the fold boundary — phase 0.99 and phase 0.01 —
        // are minutes apart in cycle terms. Folded onto a line they land at opposite
        // EDGES of the card and read as two unrelated clumps, so a genuine rhythm could
        // be hidden purely by where the range happened to start. On a ring they are
        // neighbours, because a circle has no edges.
        const double period = 10;
        var justBefore = At(From.AddDays(period).AddMinutes(-6));
        var justAfter = At(From.AddDays(period).AddMinutes(6));

        var stars = ConstellationLayout.PlaceOnRing(
            new[] { justBefore, justAfter }, From, To, period, Width, Height);

        var a = Math.Atan2(stars[0].Y - Height / 2, stars[0].X - Width / 2);
        var b = Math.Atan2(stars[1].Y - Height / 2, stars[1].X - Width / 2);

        // Shortest way round the circle, which is the only distance that means
        // anything here.
        var apart = Math.Abs(Math.Atan2(Math.Sin(a - b), Math.Cos(a - b)));

        Assert.True(apart < 0.1,
            $"two entries twelve minutes apart landed {apart:0.00} radians apart on the ring");
    }

    [Fact]
    public void Fold_IgnoresANonsensePeriodRatherThanDividingByZero()
    {
        Assert.Empty(ConstellationLayout.PlaceOnRing(new[] { At(From) }, From, To, 0, Width, Height));
        Assert.Empty(ConstellationLayout.PlaceOnRing(new[] { At(From) }, From, To, -3, Width, Height));
    }

    // ── The wall of nights ───────────────────────────────────────────────────

    [Fact]
    public void Wall_OneRowPerDay_OldestAtTheTop()
    {
        var stars = ConstellationLayout.PlaceOnWall(
            new[] { At(From.AddHours(9)), At(From.AddDays(5).AddHours(9)) },
            From, Width, rowHeight: 20);

        Assert.True(stars[0].Y < stars[1].Y, "time did not read downward");

        // Five days apart is five rows apart, give or take each star's wobble inside
        // its own row.
        Assert.InRange(stars[1].Y - stars[0].Y, 5 * 20 - 7, 5 * 20 + 7);
    }

    [Fact]
    public void Wall_TheSameHourLandsInTheSameColumn()
    {
        // The vertical band that says "these keep happening at three in the morning".
        var events = Enumerable.Range(0, 10)
            .Select(i => At(From.AddDays(i).AddHours(3)))
            .ToArray();

        var stars = ConstellationLayout.PlaceOnWall(events, From, Width, rowHeight: 20);

        foreach (var star in stars)
            Assert.Equal(stars[0].X, star.X, 6);
    }

    [Fact]
    public void Wall_TheDayRunsLeftToRight()
    {
        var stars = ConstellationLayout.PlaceOnWall(
            new[] { At(From.AddHours(1)), At(From.AddHours(23)) },
            From, Width, rowHeight: 20);

        Assert.True(stars[0].X < stars[1].X);
        Assert.True(stars[0].X >= ConstellationLayout.WallInset, "a star overlapped the dates");
        Assert.True(stars[1].X <= Width);
    }

    [Fact]
    public void Wall_ClusteredEntriesShareARowWithoutLeavingIt()
    {
        // Three seizures inside one day is a crowded ROW — the shape a lot of dogs'
        // emergency plans hang on. They must stay inside their own night, or the
        // picture says they happened on different days.
        const double rowHeight = 20;
        var day = From.AddDays(3);
        var events = new[] { At(day.AddHours(2)), At(day.AddHours(2.5)), At(day.AddHours(3)) };

        var stars = ConstellationLayout.PlaceOnWall(events, From, Width, rowHeight);

        var top = 3 * rowHeight;
        foreach (var star in stars)
            Assert.InRange(star.Y, top, top + rowHeight);
    }

    [Fact]
    public void Wall_RowsFillAShortRangeAndStopShrinkingOnALongOne()
    {
        // A week of rows should use the whole card. A year of them cannot — below a
        // point a row stops being a row, so the wall grows past the card and is
        // scrolled instead of squeezed.
        Assert.Equal(500.0 / 7, ConstellationLayout.RowHeight(7, 500), 6);
        Assert.Equal(ConstellationLayout.MinRowHeight, ConstellationLayout.RowHeight(365, 500), 6);
    }

    [Fact]
    public void Wall_CountsBothEndsOfTheRange()
    {
        Assert.Equal(1, ConstellationLayout.DayCount(From, From));
        Assert.Equal(7, ConstellationLayout.DayCount(From, From.AddDays(6)));
    }

    [Fact]
    public void Fold_CannotBeSetLongerThanHalfTheStretch()
    {
        // You cannot see a repeat in a window that does not hold two of them. The
        // bound is what the picture is CAPABLE of showing — never what the answer is.
        Assert.Equal(3, MaxFold(7));
        Assert.Equal(15, MaxFold(30));
        Assert.Equal(45, MaxFold(90));
        Assert.Equal(60, MaxFold(365));   // and never past the dial's own ceiling
        Assert.Equal(2, MaxFold(1));      // always at least a two-day fold to offer

        static double MaxFold(int rangeDays) => Math.Max(2, Math.Min(60, rangeDays / 2));
    }

    // ── Neither lens touches the records ─────────────────────────────────────

    [Fact]
    public void EveryLensPlacesTheSameEventsOneForOne()
    {
        // Index is identity everywhere — the detail sheet, the hit test and the drawing
        // all key off it, so a lens that dropped or reordered a star would describe the
        // wrong entry when one was tapped.
        var events = Enumerable.Range(0, 40)
            .Select(i => At(From.AddHours(i * 13))).ToArray();

        Assert.Equal(events.Length,
            ConstellationLayout.Place(events, From, To, Width, Height, Sky).Length);
        Assert.Equal(events.Length,
            ConstellationLayout.PlaceOnRing(events, From, To, 1, Width, Height).Length);
        Assert.Equal(events.Length,
            ConstellationLayout.PlaceOnRing(events, From, To, 14, Width, Height).Length);
        Assert.Equal(events.Length,
            ConstellationLayout.PlaceOnWall(events, From, Width, 20).Length);
    }
}
