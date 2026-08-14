namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// Where the Constellation puts a star, and — more importantly — what it refuses to
/// let a position mean.
///
/// <para>The surface has exactly one rule: <b>position is when something happened,
/// symbol is what happened, density is how much was recorded, and nothing else is
/// encoded.</b> That rule lives entirely in this class, and it is the kind of thing
/// that breaks silently: a fan that grew without bound would turn a busy afternoon
/// into a spike, and a spike in a health app reads as "worse", which is a claim
/// Felova is not allowed to make about anyone's animal.</para>
/// </summary>
public class ConstellationLayoutTests
{
    private static readonly DateTime From = new(2026, 1, 1);
    private static readonly DateTime To = new(2026, 1, 31);

    /// <summary>The hand-tuned middle of every band — the sky a pet with no name
    /// gets, and the one these tests measure against.</summary>
    private static readonly SkySignature Sky = SkySignature.Default;

    private const double Width = 900;
    private const double Height = 300;

    private static CelestialEvent At(DateTime when, CelestialCategory category = CelestialCategory.Mood) =>
        new(when, category, "x", string.Empty);

    // ── Time is the axis ─────────────────────────────────────────────────────

    [Fact]
    public void XFor_MapsTheRangeOntoTheCanvas()
    {
        Assert.Equal(0, ConstellationLayout.XFor(From, From, To, Width), 3);
        Assert.Equal(Width, ConstellationLayout.XFor(To, From, To, Width), 3);
    }

    [Fact]
    public void XFor_IsLinearInTime()
    {
        // Halfway through the range is halfway across the canvas, at every zoom. A
        // non-linear axis would make "these two happened close together" a lie.
        var middle = From.AddTicks((To - From).Ticks / 2);
        Assert.Equal(Width / 2, ConstellationLayout.XFor(middle, From, To, Width), 3);
    }

    [Fact]
    public void XFor_ZeroLengthRange_CentresRatherThanDividingByZero()
    {
        Assert.Equal(Width / 2, ConstellationLayout.XFor(From, From, From, Width), 3);
    }

    // ── The path carries no data ─────────────────────────────────────────────

    [Fact]
    public void PathY_DependsOnPositionAlone()
    {
        // Deliberately pinned: the moment the line's height could be derived from
        // anything recorded, a rising path would read as "getting better".
        Assert.Equal(
            ConstellationLayout.PathY(412, Height, Sky),
            ConstellationLayout.PathY(412, Height, Sky),
            9);

        Assert.NotEqual(
            ConstellationLayout.PathY(0, Height, Sky),
            ConstellationLayout.PathY(140, Height, Sky),
            9);
    }

    [Fact]
    public void PathY_StaysInsideTheCanvas()
    {
        for (double x = 0; x < 4000; x += 7)
        {
            var y = ConstellationLayout.PathY(x, Height, Sky);
            Assert.InRange(y, 0, Height);
        }
    }

    // ── Density is a size, not a summary ─────────────────────────────────────

    [Fact]
    public void StarRadius_ShrinksAsTheSkyFillsUp()
    {
        // The same canvas, five entries vs five hundred. Nothing is aggregated; the
        // symbols simply take the room they have.
        var sparse = ConstellationLayout.StarRadius(Width, 5);
        var dense = ConstellationLayout.StarRadius(Width, 500);

        Assert.True(dense < sparse);
        Assert.True(dense >= 2.0, $"a star shrank to {dense:0.0}, below the point where a shape reads");
    }

    [Fact]
    public void StarRadius_GrowsBackAsYouZoomIn()
    {
        // Zooming widens the content, so each entry has more room and the symbols
        // return to full size — the progressive reveal, with no second data path.
        var out_ = ConstellationLayout.StarRadius(Width, 400);
        var in_ = ConstellationLayout.StarRadius(Width * 8, 400);

        Assert.True(in_ > out_);
    }

    [Fact]
    public void StarRadius_EmptySky_IsNotADivideByZero()
    {
        Assert.True(ConstellationLayout.StarRadius(Width, 0) > 0);
        Assert.True(ConstellationLayout.StarRadius(0, 10) > 0);
    }

    // ── Placement ────────────────────────────────────────────────────────────

    [Fact]
    public void Place_ReturnsOneStarPerEventInInputOrder()
    {
        // Index is identity — the hit test, the detail card and the drawing all key
        // off it, so a reordering here would show the wrong entry for a tapped star.
        var events = new[]
        {
            At(new DateTime(2026, 1, 2, 8, 0, 0)),
            At(new DateTime(2026, 1, 20, 8, 0, 0)),
            At(new DateTime(2026, 1, 9, 8, 0, 0)),
        };

        var stars = ConstellationLayout.Place(events, From, To, Width, Height, Sky);

        Assert.Equal(3, stars.Length);
        Assert.True(stars[0].X < stars[2].X);
        Assert.True(stars[2].X < stars[1].X);
    }

    [Fact]
    public void Place_EmptyOrUnsizedCanvas_PlacesNothing()
    {
        Assert.Empty(ConstellationLayout.Place(Array.Empty<CelestialEvent>(), From, To, Width, Height, Sky));
        Assert.Empty(ConstellationLayout.Place(new[] { At(From) }, From, To, 0, Height, Sky));
        Assert.Empty(ConstellationLayout.Place(new[] { At(From) }, From, To, Width, 0, Sky));
    }

    [Fact]
    public void Place_StarsSitNearTheirOwnTime()
    {
        var when = new DateTime(2026, 1, 15, 12, 0, 0);
        var stars = ConstellationLayout.Place(new[] { At(when) }, From, To, Width, Height, Sky);

        Assert.Equal(ConstellationLayout.XFor(when, From, To, Width), stars[0].X, 3);
    }

    [Fact]
    public void Place_LoneStarSitsOnTheLine()
    {
        var stars = ConstellationLayout.Place(
            new[] { At(new DateTime(2026, 1, 15, 12, 0, 0)) }, From, To, Width, Height, Sky);

        // Close enough that no guide line is drawn: with nothing to be crowded by,
        // a star is on the timeline rather than floating above it.
        Assert.True(Math.Abs(stars[0].Offset) <= ConstellationLayout.GuideThreshold);
    }

    // ── Density is a patch, never a peak ─────────────────────────────────────

    [Fact]
    public void Place_ACrowdFansToBothSidesOfTheLine()
    {
        // Twelve things written down in the same hour. They must spread around the
        // line, not stack upward off it.
        var when = new DateTime(2026, 1, 15, 12, 0, 0);
        var events = Enumerable.Range(0, 12)
            .Select(i => At(when.AddMinutes(i)))
            .ToArray();

        var stars = ConstellationLayout.Place(events, From, To, Width, Height, Sky);

        Assert.Contains(stars, s => s.Offset < -ConstellationLayout.GuideThreshold);
        Assert.Contains(stars, s => s.Offset > ConstellationLayout.GuideThreshold);
    }

    [Fact]
    public void Place_ADenseDayNeverGrowsTaller()
    {
        // The whole point. Five hundred entries at one moment must not reach further
        // from the line than a dozen do — density fills the sky, it does not build a
        // column that could be read as severity.
        var when = new DateTime(2026, 1, 15, 12, 0, 0);

        var few = ConstellationLayout.Place(
            Enumerable.Range(0, 12).Select(i => At(when.AddSeconds(i))).ToArray(),
            From, To, Width, Height, Sky);

        var many = ConstellationLayout.Place(
            Enumerable.Range(0, 500).Select(i => At(when.AddSeconds(i))).ToArray(),
            From, To, Width, Height, Sky);

        var fewReach = few.Max(s => Math.Abs(s.Offset));
        var manyReach = many.Max(s => Math.Abs(s.Offset));

        // Forty times the entries, and the cluster is the same height (the slack is
        // the per-entry jitter, which more samples simply explore more of).
        Assert.True(manyReach <= fewReach * 1.2,
            $"a dense cluster reached {manyReach:0.0} where a sparse one reached {fewReach:0.0}");

        // And in absolute terms it stays a patch beside the line, not a wall.
        Assert.True(manyReach < Height / 4,
            $"a dense cluster reached {manyReach:0.0} on a {Height:0} canvas");
    }

    [Fact]
    public void Place_ACrowdUsesTheCanvasItHas()
    {
        // The fan is a fraction of the height, not a fixed number of pixels. A tall
        // canvas must actually be used — the first version fanned the same ~57 units
        // either way, which smeared a month of logging into a band across the middle
        // while most of the screen sat empty.
        var when = new DateTime(2026, 1, 15, 12, 0, 0);
        var events = Enumerable.Range(0, 24).Select(i => At(when.AddSeconds(i * 30))).ToArray();

        var shortReach = ConstellationLayout.Place(events, From, To, Width, 300, Sky)
            .Max(s => Math.Abs(s.Offset));
        var tallReach = ConstellationLayout.Place(events, From, To, Width, 900, Sky)
            .Max(s => Math.Abs(s.Offset));

        Assert.True(tallReach > shortReach * 2,
            $"a canvas three times taller only reached {tallReach:0.0} against {shortReach:0.0}");
    }

    [Fact]
    public void Place_StarsStayInsideTheCanvas()
    {
        var when = new DateTime(2026, 1, 15, 12, 0, 0);
        var events = Enumerable.Range(0, 400).Select(i => At(when.AddSeconds(i))).ToArray();

        foreach (var star in ConstellationLayout.Place(events, From, To, Width, Height, Sky))
            Assert.InRange(star.Y, 0, Height);
    }

    [Fact]
    public void Place_IsStableAcrossRedraws()
    {
        // Stars must not swim when the canvas repaints — the jitter is derived from
        // each entry's own instant, never from a random source.
        var events = Enumerable.Range(0, 40)
            .Select(i => At(From.AddHours(i * 7)))
            .ToArray();

        var first = ConstellationLayout.Place(events, From, To, Width, Height, Sky);
        var second = ConstellationLayout.Place(events, From, To, Width, Height, Sky);

        Assert.Equal(first, second);
    }

    // ── Tapping ──────────────────────────────────────────────────────────────

    [Fact]
    public void HitTest_EmptySky_SelectsNothing()
    {
        var stars = ConstellationLayout.Place(new[] { At(From.AddDays(2)) }, From, To, Width, Height, Sky);

        Assert.Equal(-1, ConstellationLayout.HitTest(stars, 800, 20, 22, zoom: 1));
    }

    [Fact]
    public void HitTest_PicksTheNearestStar()
    {
        var events = new[]
        {
            At(new DateTime(2026, 1, 5, 0, 0, 0)),
            At(new DateTime(2026, 1, 6, 0, 0, 0)),
            At(new DateTime(2026, 1, 25, 0, 0, 0)),
        };

        var stars = ConstellationLayout.Place(events, From, To, Width, Height, Sky);
        var target = stars[1];

        Assert.Equal(1, ConstellationLayout.HitTest(stars, target.X + 1, target.Y + 1, 22, zoom: 1));
    }

    [Fact]
    public void HitTest_ZoomedIn_StopsSweepingUpTheNeighbours()
    {
        // Two entries a day apart. Zoomed out they are a few units apart in world
        // space and both fall inside the reach; zoomed right in they are far apart on
        // screen, and only the one actually under the fingertip may answer.
        var events = new[]
        {
            At(new DateTime(2026, 1, 10, 0, 0, 0)),
            At(new DateTime(2026, 1, 11, 0, 0, 0)),
        };

        var stars = ConstellationLayout.Place(events, From, To, Width, Height, Sky);
        var between = (stars[0].X + stars[1].X) / 2;

        // Halfway between them at 40×, each is ~600 screen units away: neither counts.
        Assert.Equal(-1, ConstellationLayout.HitTest(stars, between, stars[0].Y, 22, zoom: 40));

        // On top of the first one, it still answers however far in you are.
        Assert.Equal(0, ConstellationLayout.HitTest(stars, stars[0].X, stars[0].Y, 22, zoom: 40));
    }
}
