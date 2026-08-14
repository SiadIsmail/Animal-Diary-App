namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Where every star sits. Pure arithmetic, no MAUI — which is the point: this is
//  the whole of the Constellation's meaning ("position is when it happened"), and
//  it is compile-linked into the test project so that meaning can be pinned down
//  without a device.
//
//  Everything is computed in CONTENT space: x runs 0..contentWidth for the whole
//  visible stretch of time, y runs 0..contentHeight. Zooming widens contentWidth
//  and re-places; panning is a translate the drawable applies afterwards. Placement
//  therefore never depends on the scroll position, so a star does not drift as the
//  sky moves under it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One placed star. <see cref="PathY"/> rides along because the guide line
/// that ties a star to the timeline is drawn between the two, and re-deriving the
/// path's y in the drawable would be the same formula written twice.</summary>
/// <param name="LinkTo">The star just before this one in the same cluster, or -1.
/// Purely so the drawing can join things that happened at the SAME MOMENT with a
/// faint line — which is what turns a scatter into a constellation. It encodes
/// nothing new: two entries a minute apart are already drawn a minute apart, and the
/// line only says so out loud. It is never drawn between separate moments, and never
/// implies an order or a direction.</param>
public readonly record struct SkyStar(double X, double Y, double PathY, int LinkTo = -1)
{
    /// <summary>How far off the timeline this star sits. Positive is below.</summary>
    public double Offset => Y - PathY;
}

public static class ConstellationLayout
{
    // ── The path ──────────────────────────────────────────────────────────────
    // Two sine waves of unrelated wavelength, so the line never repeats visibly and
    // never looks like a plot. It is DECORATION, and it must stay that way: the
    // wave's height at a given moment is a consequence of x alone and says nothing
    // about the pet. Deriving it from anything recorded would turn a rising line
    // into "getting better", which is precisely the claim this app never makes.
    //
    // Its numbers — phase, the two wavelengths, the mix and the amplitude — come from
    // the pet's SkySignature, inside bands narrow enough that no pet can draw a bad
    // sky, only a different one. The middle of every band is the hand-tuned original
    // (SkySignature.Default): a long sweep of ~330 canvas units and a ripple of ~107,
    // which is two full turns across a phone's width using most of the canvas height.
    // The version before that used a tenth of the amplitude and a sweep five times
    // longer, and pinned every star into one flat band across the middle.

    // ── The fan ───────────────────────────────────────────────────────────────
    // Stars that land within ClusterWindow of one another step outward from the path
    // in alternating rings instead of stacking into a column. A column would read as
    // a bar — a height, a peak, a "bad day" — which is the one thing the whole
    // surface is built to avoid. Wrapping at RingCount keeps a busy afternoon a
    // DENSE PATCH rather than an ever-taller spike: more recorded looks like more
    // sky used, never like something worse happening.
    //
    // The gaps are FRACTIONS OF THE CANVAS, not pixels. Fixed pixels meant a crowd
    // fanned the same 57 units whether it had 40 units of sky or 700, so a month of
    // real logging turned into a smear along the line while most of the screen sat
    // empty.
    private const double ClusterWindow = 14.0;
    private const double BaseGapFraction = 0.035;
    private const double RingStepFraction = 0.028;
    private const int RingCount = 6;
    private const double JitterAmplitude = 3.5;

    // ── Star size ─────────────────────────────────────────────────────────────
    // Below MinRadius a symbol stops being a symbol — an eight-point burst three
    // pixels across is a dot, and the whole point is that the SHAPE says what
    // happened. The floor is deliberately high enough that a crowded sky overlaps
    // rather than dissolving; zooming is what separates it. Above MaxRadius a quiet
    // week looks like clip art.
    private const double MinRadius = 3.1;
    private const double MaxRadius = 5.6;

    /// <summary>Below this, a star reads as sitting ON the line and needs no guide.</summary>
    public const double GuideThreshold = 7.0;

    /// <summary>Kept clear at the top and bottom so a star never touches the frame.</summary>
    public const double VerticalPadding = 12.0;

    /// <summary>The timeline's height at a content x. Depends on x, the canvas height
    /// and the pet's signature only — see the note above on why it may never depend on
    /// data.</summary>
    public static double PathY(double x, double contentHeight, in SkySignature signature)
    {
        var mid = contentHeight / 2.0;
        var amplitude = contentHeight * signature.Amplitude;
        var wave = signature.LongWeight * Math.Sin(x / signature.LongWavelength + signature.PhaseLong)
                 + signature.ShortWeight * Math.Sin(x / signature.ShortWavelength + signature.PhaseShort);
        return mid + amplitude * wave;
    }

    /// <summary>
    /// How big a star is drawn, from how much room each one has. This is the
    /// <b>density</b> half of the surface's rule, and it belongs here rather than in
    /// the drawable because it is the same arithmetic the fan needs.
    ///
    /// <para>Derived from the SPACING between events rather than their count: a
    /// thousand entries across a zoomed-in year have as much elbow room each as five
    /// across a week, and should look it. It is also what makes zooming a progressive
    /// reveal — the content widens, the spacing grows, and the symbols grow back into
    /// themselves without anything being re-summarised.</para>
    /// </summary>
    public static double StarRadius(double contentWidth, int count)
    {
        if (count <= 0 || contentWidth <= 0)
            return MaxRadius;

        return Math.Clamp(contentWidth / count * 0.7, MinRadius, MaxRadius);
    }

    /// <summary>Content x for a moment. The single place time becomes distance —
    /// linear, so equal gaps in time are equal gaps on screen at every zoom.</summary>
    public static double XFor(DateTime when, DateTime from, DateTime to, double contentWidth)
    {
        var span = (to - from).Ticks;
        if (span <= 0)
            return contentWidth / 2.0;

        var t = (when - from).Ticks / (double)span;
        return t * contentWidth;
    }

    /// <summary>
    /// Place every event. Input is expected in ascending time order (the service
    /// returns it that way); out of order input still places correctly, it just fans
    /// its clusters in a different arrangement.
    /// </summary>
    /// <returns>One star per event, in the SAME order as <paramref name="events"/>,
    /// so index is identity — the detail card, the hit test and the drawing all key
    /// off it.</returns>
    public static SkyStar[] Place(
        IReadOnlyList<CelestialEvent> events,
        DateTime from,
        DateTime to,
        double contentWidth,
        double contentHeight,
        in SkySignature signature)
    {
        if (events.Count == 0 || contentWidth <= 0 || contentHeight <= 0)
            return Array.Empty<SkyStar>();

        var count = events.Count;
        var xs = new double[count];
        for (int i = 0; i < count; i++)
            xs[i] = XFor(events[i].When, from, to, contentWidth);

        // Walk left to right so "how many are already crowded around this x" is just a
        // sliding window, rather than a pairwise comparison — which is what keeps a
        // year of five thousand entries affordable to re-place on every pinch.
        var order = new int[count];
        for (int i = 0; i < count; i++)
            order[i] = i;
        Array.Sort(order, (a, b) => xs[a].CompareTo(xs[b]));

        var stars = new SkyStar[count];
        var lo = 0;

        for (int rank = 0; rank < count; rank++)
        {
            var i = order[rank];
            var x = xs[i];

            while (xs[order[lo]] < x - ClusterWindow)
                lo++;

            var crowd = rank - lo;
            var pathY = PathY(x, contentHeight, signature);
            var offset = OffsetFor(crowd, events[i].When.Ticks, contentHeight);

            var y = Math.Clamp(
                pathY + offset,
                VerticalPadding,
                contentHeight - VerticalPadding);

            // Chain each star to the one before it, but only inside a cluster — the
            // link is "these happened together", so the first star of a moment starts
            // a new constellation rather than reaching back to the last one.
            var link = crowd > 0 ? order[rank - 1] : -1;

            stars[i] = new SkyStar(x, y, pathY, link);
        }

        return stars;
    }

    /// <summary>How far from the line the n-th star of a cluster sits. Exposed for the
    /// tests that pin the fan's shape: alternating sides, bounded height.</summary>
    public static double OffsetFor(int crowd, long seed, double contentHeight)
    {
        var jitter = Jitter(seed, JitterAmplitude);
        if (crowd <= 0)
            return jitter;

        // 1,2 → first ring above/below; 3,4 → second; and so on, wrapping at RingCount.
        var ring = ((crowd + 1) / 2 - 1) % RingCount;
        var sign = crowd % 2 == 1 ? -1.0 : 1.0;
        var gap = contentHeight * (BaseGapFraction + ring * RingStepFraction);
        return sign * gap + jitter;
    }

    /// <summary>
    /// The star nearest a point, or -1 when the tap landed on empty sky. Nearest
    /// rather than first-hit so overlapping stars in a dense patch resolve to the one
    /// actually under the fingertip.
    /// </summary>
    /// <param name="worldX">Tap position along the world's x, i.e. before the zoom.</param>
    /// <param name="y">Tap position down the canvas. Never scaled — only time zooms.</param>
    /// <param name="reach">How near counts as a hit, in SCREEN units.</param>
    /// <param name="zoom">The camera's magnification, so the reach means the same
    /// distance on screen at every zoom. Without it a zoomed-in tap would have to land
    /// within a fraction of a world unit, and a zoomed-out one would sweep up half a
    /// week.</param>
    public static int HitTest(IReadOnlyList<SkyStar> stars, double worldX, double y, double reach, double zoom)
    {
        var best = -1;
        var bestSquared = reach * reach;
        var scale = zoom <= 0 ? 1 : zoom;

        for (int i = 0; i < stars.Count; i++)
        {
            var dx = (stars[i].X - worldX) * scale;
            if (dx > reach || dx < -reach)
                continue;

            var dy = stars[i].Y - y;
            var squared = dx * dx + dy * dy;
            if (squared <= bestSquared)
            {
                bestSquared = squared;
                best = i;
            }
        }

        return best;
    }

    /// <summary>A stable ±<paramref name="amplitude"/> wobble from the entry's own
    /// instant, so the same sky redraws identically forever. Random jitter would make
    /// stars swim on every invalidate — and this surface is looked at, not scrolled
    /// past.</summary>
    private static double Jitter(long seed, double amplitude)
    {
        unchecked
        {
            var h = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            return (h % 2000 / 1000.0 - 1.0) * amplitude;
        }
    }
}
