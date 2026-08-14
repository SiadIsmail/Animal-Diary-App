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

/// <summary>
/// Which way the sky is folded. Three seats in the same room, looking at exactly the
/// same entries — the arrangement changes, the records never do.
///
/// <para>Time is still the only thing any of them encodes. What differs is <b>which
/// question the axis answers</b>: when did this happen, when in the DAY did it happen,
/// and does it come round again. A list and a calendar can both answer the first;
/// neither can answer the other two, which is the entire reason these exist.</para>
/// </summary>
public enum SkyLens
{
    /// <summary>Straight time, left to right. The home view, and the one that is shared.</summary>
    Timeline,

    /// <summary>A 24-hour dial. Angle is the time of day; distance from the centre is
    /// how far through the stretch the day was.</summary>
    Clock,

    /// <summary>Time folded at a period the owner chooses, so the history lies over
    /// itself and a repeat shows as an alignment.</summary>
    Rhythm
}

/// <summary>One placed star. <see cref="PathY"/> rides along because the guide line
/// that ties a star to the timeline is drawn between the two, and re-deriving the
/// path's y in the drawable would be the same formula written twice.</summary>
/// <param name="LinkTo">The star just before this one in the same cluster, or -1.
/// Purely so the drawing can join things that happened at the SAME MOMENT with a
/// faint line — which is what turns a scatter into a constellation. It encodes
/// nothing new: two entries a minute apart are already drawn a minute apart, and the
/// line only says so out loud. It is never drawn between separate moments, and never
/// implies an order or a direction.</param>
/// <param name="NudgeX">A few SCREEN pixels sideways, so a knot of entries reads as a
/// cluster of stars rather than a stack.
///
/// <para>It is kept out of <see cref="X"/>, and that separation is load-bearing: X is
/// WHEN, and it is multiplied by the zoom. A world-space sideways offset would grow
/// with the magnification until it dwarfed the real gaps between entries and started
/// reordering them. This one is applied at draw time and <b>fades out as you zoom
/// in</b> — it exists only while the picture genuinely cannot separate the moments,
/// and by the time it can, it is gone.</para></param>
public readonly record struct SkyStar(double X, double Y, double PathY, int LinkTo = -1, double NudgeX = 0)
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

    // ── The scatter ───────────────────────────────────────────────────────────
    // Stars that land within ClusterWindow of one another are arranged around their
    // moment on a SUNFLOWER spiral: each one turned by the golden angle from the last
    // and set at sqrt(k) × the spacing, which is how nature packs a disc evenly.
    //
    // It replaced a fan of alternating rings, which stepped straight up and down and
    // so drew a knot of entries as a COLUMN — a bar in all but name, and the one
    // reading this surface exists to avoid. A spiral fills a disc instead: more
    // recorded is a wider patch of sky, never a taller spike.
    //
    // Wrapping at Capacity is what holds that promise exactly: the twelfth star of a
    // cluster is as far out as the five hundredth, so a frantic afternoon and a busy
    // one are the same size on the page.
    //
    // The spacing is a FRACTION OF THE CANVAS, not a pixel count. Fixed pixels meant
    // a crowd spread the same 57 units whether it had 40 units of sky or 700, so a
    // month of real logging smeared along the line while the screen sat empty.
    private const double ClusterWindow = 14.0;
    private const double SpiralStepFraction = 0.075;
    private const int Capacity = 12;
    private const double GoldenAngle = 2.399963229728653;

    /// <summary>How much of the spiral is allowed to act sideways. Tiny, because
    /// sideways is TIME — see <see cref="SkyStar.NudgeX"/>. It is enough to break a
    /// stack into a cluster and never enough to move an entry past its neighbour.
    /// </summary>
    private const double SidewaysSquash = 0.055;

    /// <summary>The stable wobble on a star that has no crowd to be arranged in. Also
    /// the most a lone star can sit off the line.</summary>
    public const double JitterAmplitude = 3.5;

    // ── Star size ─────────────────────────────────────────────────────────────
    // Below MinRadius a symbol stops being a symbol — an eight-point burst three
    // pixels across is a dot, and the whole point is that the SHAPE says what
    // happened. The floor is deliberately high enough that a crowded sky overlaps
    // rather than dissolving; zooming is what separates it. Above MaxRadius a quiet
    // week looks like clip art.
    private const double MinRadius = 3.1;
    private const double MaxRadius = 5.6;

    // ── The dial (the Clock lens) ─────────────────────────────────────────────
    /// <summary>Kept clear around the dial so no star is cut by the card's corner.</summary>
    private const double DialPadding = 26.0;

    /// <summary>The hole in the middle of the dial. Without it every entry from the
    /// first day of the range would land on one point.</summary>
    private const double DialInnerFraction = 0.22;

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

        var xs = new double[events.Count];
        for (int i = 0; i < events.Count; i++)
            xs[i] = XFor(events[i].When, from, to, contentWidth);

        return AlongAxis(events, xs, contentHeight, signature);
    }

    /// <summary>
    /// <b>The Rhythm lens.</b> The same horizon, with time FOLDED: every moment is
    /// placed by how far it sits into a repeating period rather than by its date, so
    /// the whole history is laid over itself.
    ///
    /// <para>This is the one arrangement that can answer "does it come round again?".
    /// On a straight axis a twelve-day rhythm is a row of dots somewhat evenly spaced,
    /// which no eye can read; folded at twelve days it is a <b>vertical alignment</b>,
    /// and folded at anything else it stays a smear. The owner turns the dial and the
    /// pattern either crystallises or it does not.</para>
    ///
    /// <para><b>The app never chooses the period.</b> It draws whatever fold it is
    /// handed and says nothing about the result — no "cycle detected", no highlight, no
    /// number. An alignment is something the person looking sees, not something Felova
    /// claims. That distinction is the whole reason this is allowed to exist.</para>
    /// </summary>
    /// <param name="periodDays">The fold, in days.</param>
    public static SkyStar[] PlaceFolded(
        IReadOnlyList<CelestialEvent> events,
        DateTime from,
        double periodDays,
        double contentWidth,
        double contentHeight,
        in SkySignature signature)
    {
        if (events.Count == 0 || contentWidth <= 0 || contentHeight <= 0 || periodDays <= 0)
            return Array.Empty<SkyStar>();

        var xs = new double[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var elapsed = (events[i].When - from).TotalDays;
            var phase = elapsed / periodDays;
            phase -= Math.Floor(phase);          // 0..1 through the period
            xs[i] = phase * contentWidth;
        }

        return AlongAxis(events, xs, contentHeight, signature);
    }

    /// <summary>
    /// <b>The Clock lens.</b> A dial: the angle is the TIME OF DAY, the distance from
    /// the centre is how far through the chosen stretch the day was.
    ///
    /// <para>Both are still only time, which is what keeps the rule intact — but
    /// arranged this way, "almost all of these happen between two and five in the
    /// morning" is a wedge you see at a glance instead of a fact buried in a list.
    /// Same time of day on different dates lines up as a SPOKE.</para>
    ///
    /// <para>Fixed to the canvas: a clock is not panned or zoomed, so the camera does
    /// not apply here and <see cref="SkyStar.X"/> is a canvas position rather than a
    /// moment.</para>
    /// </summary>
    public static SkyStar[] PlaceOnDial(
        IReadOnlyList<CelestialEvent> events,
        DateTime from,
        DateTime to,
        double width,
        double height)
    {
        if (events.Count == 0 || width <= 0 || height <= 0)
            return Array.Empty<SkyStar>();

        var centreX = width / 2;
        var centreY = height / 2;
        var outer = Math.Min(width, height) / 2 - DialPadding;
        if (outer <= 0)
            return Array.Empty<SkyStar>();

        // The middle is left empty on purpose. Everything from the first day of the
        // range would otherwise pile onto one point, and the oldest entries would be
        // the least legible — the opposite of useful.
        var inner = outer * DialInnerFraction;
        var span = (to - from).Ticks;

        var stars = new SkyStar[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var when = events[i].When;

            // Midnight at the top, running clockwise, like every clock face.
            var dayFraction = when.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay;
            var angle = dayFraction * Math.Tau - Math.PI / 2;

            var through = span <= 0 ? 1 : Math.Clamp((when - from).Ticks / (double)span, 0, 1);
            var radius = inner + through * (outer - inner);

            // A whisper of jitter so two entries at the same minute of the same day are
            // two stars rather than one. Small enough that a tight wedge stays tight.
            var wobble = Jitter(when.Ticks, 1.0);
            angle += wobble * 0.012;
            radius += wobble * 1.6;

            var x = centreX + Math.Cos(angle) * radius;
            var y = centreY + Math.Sin(angle) * radius;

            stars[i] = new SkyStar(x, y, y);
        }

        return stars;
    }

    /// <summary>The shared placer: given where each event sits along the horizontal
    /// axis, hang it off the horizon and spiral its crowd. Both the Timeline and the
    /// Rhythm lens are this — they differ only in what the axis MEANS.</summary>
    private static SkyStar[] AlongAxis(
        IReadOnlyList<CelestialEvent> events,
        double[] xs,
        double contentHeight,
        in SkySignature signature)
    {
        var count = events.Count;

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
            var (nudgeX, offset) = Arrange(crowd, events[i].When.Ticks, contentHeight);

            var y = Math.Clamp(
                pathY + offset,
                VerticalPadding,
                contentHeight - VerticalPadding);

            // Chain each star to the one before it, but only inside a cluster — the
            // link is "these happened together", so the first star of a moment starts
            // a new constellation rather than reaching back to the last one.
            var link = crowd > 0 ? order[rank - 1] : -1;

            stars[i] = new SkyStar(x, y, pathY, link, nudgeX);
        }

        return stars;
    }

    /// <summary>
    /// Where the n-th star of a cluster sits relative to its moment: a point on the
    /// sunflower spiral, squashed hard sideways because sideways is time.
    /// </summary>
    /// <returns>A screen-space sideways nudge (see <see cref="SkyStar.NudgeX"/>) and a
    /// vertical offset from the timeline.</returns>
    public static (double NudgeX, double OffsetY) Arrange(int crowd, long seed, double contentHeight)
    {
        var jitter = Jitter(seed, JitterAmplitude);
        if (crowd <= 0)
            return (0, jitter);

        // sqrt(k) × spacing at the golden angle — an even disc. k wraps at Capacity so
        // the disc stops growing; the ANGLE keeps counting, so a wrapped star lands
        // between the earlier ones rather than on top of one.
        var k = crowd % Capacity;
        var radius = contentHeight * SpiralStepFraction * Math.Sqrt(k);
        var angle = crowd * GoldenAngle;

        return (Math.Cos(angle) * radius * SidewaysSquash,
                Math.Sin(angle) * radius + jitter);
    }

    /// <summary>
    /// How much of <see cref="SkyStar.NudgeX"/> still applies at this magnification.
    /// Full at rest, gone by the time the zoom has genuinely pulled a cluster apart —
    /// the nudge is a way of drawing moments the picture cannot separate, so once it
    /// can, the honest positions take over.
    /// </summary>
    public static double NudgeFade(double zoom) => Math.Clamp(1 - (zoom - 1) / 3.0, 0, 1);

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
    /// week. It also decides how much of each star's sideways nudge still applies —
    /// the test has to hunt where the star was DRAWN, not where its moment is.</param>
    public static int HitTest(IReadOnlyList<SkyStar> stars, double worldX, double y, double reach, double zoom)
    {
        var best = -1;
        var bestSquared = reach * reach;
        var scale = zoom <= 0 ? 1 : zoom;
        var fade = NudgeFade(scale);

        for (int i = 0; i < stars.Count; i++)
        {
            var dx = (stars[i].X - worldX) * scale + stars[i].NudgeX * fade;
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
