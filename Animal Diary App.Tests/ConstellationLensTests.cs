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
        var midnight = ConstellationLayout.PlaceOnDial(
            new[] { At(To.AddDays(-1)) }, From, To, Width, Height)[0];
        var sixAm = ConstellationLayout.PlaceOnDial(
            new[] { At(To.AddDays(-1).AddHours(6)) }, From, To, Width, Height)[0];
        var noon = ConstellationLayout.PlaceOnDial(
            new[] { At(To.AddDays(-1).AddHours(12)) }, From, To, Width, Height)[0];

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

        var stars = ConstellationLayout.PlaceOnDial(events, From, To, Width, Height);

        var bearings = stars
            .Select(s => Math.Atan2(s.Y - Height / 2, s.X - Width / 2))
            .ToArray();

        foreach (var bearing in bearings)
            Assert.Equal(bearings[0], bearing, 1);
    }

    [Fact]
    public void Dial_OlderSitsInnerAndNewerSitsOuter()
    {
        var stars = ConstellationLayout.PlaceOnDial(
            new[] { At(From.AddHours(9)), At(To.AddDays(-1).AddHours(9)) },
            From, To, Width, Height);

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
        var stars = ConstellationLayout.PlaceOnDial(events, From, To, Width, Height);

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

        foreach (var star in ConstellationLayout.PlaceOnDial(events, From, To, Width, Height))
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

        var stars = ConstellationLayout.PlaceFolded(events, From, 12, Width, Height, Sky);

        foreach (var star in stars)
            Assert.Equal(stars[0].X, star.X, 6);
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

        var stars = ConstellationLayout.PlaceFolded(events, From, 7, Width, Height, Sky);

        var spread = stars.Max(s => s.X) - stars.Min(s => s.X);
        Assert.True(spread > Width * 0.4,
            $"a mismatched fold still huddled inside {spread:0} of {Width:0}");
    }

    [Fact]
    public void Fold_EveryStarLandsInsideOneTurn()
    {
        var events = Enumerable.Range(0, 300)
            .Select(i => At(From.AddHours(i * 5))).ToArray();

        foreach (var star in ConstellationLayout.PlaceFolded(events, From, 9, Width, Height, Sky))
            Assert.InRange(star.X, 0, Width);
    }

    [Fact]
    public void Fold_IgnoresANonsensePeriodRatherThanDividingByZero()
    {
        Assert.Empty(ConstellationLayout.PlaceFolded(new[] { At(From) }, From, 0, Width, Height, Sky));
        Assert.Empty(ConstellationLayout.PlaceFolded(new[] { At(From) }, From, -3, Width, Height, Sky));
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
            ConstellationLayout.PlaceOnDial(events, From, To, Width, Height).Length);
        Assert.Equal(events.Length,
            ConstellationLayout.PlaceFolded(events, From, 14, Width, Height, Sky).Length);
    }
}
