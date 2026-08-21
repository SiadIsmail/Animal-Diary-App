namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Where every star sits. Pure arithmetic, no MAUI, which is the point: this is
//  the whole of the Constellation's meaning, and it is compile-linked into the test
//  project so that meaning can be pinned down without a device.
//
//  ── BOTH AXES MEAN SOMETHING, AND THAT IS THE WHOLE DESIGN ──
//
//  An earlier version had x = date and y = a decorative scatter around a decorative
//  wave. It was pretty and it was unreadable: shown to someone new, the honest
//  reaction was "I don't understand anything on this screen". A meaningless axis
//  cannot be rescued by making it beautiful, and everything that had been added to
//  make it interesting (a meandering path, a sunflower scatter, hairlines between
//  entries, a figure of stars behind it) was ornament competing with the data.
//
//  Y is now the TIME OF DAY. Nothing changed about what may be encoded: both axes
//  are still time, nothing is aggregated, ranked, or coloured by value, but the
//  picture answers questions now:
//
//    • where in the day something happens   → its height
//    • how often, and whether that changed  → how the dots crowd left to right
//    • several in one day                   → a vertical stack at one date
//    • a routine                            → a horizontal track (two daily doses
//                                             draw two clean lines across the sky)
//
//  That last one is the discovery this surface exists for: seizures scattered
//  against the two straight tracks of a dosing schedule is something an owner can
//  read at a glance and could never see in a list.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Which way the sky is arranged. Two, and each has one job it does better than any
/// list or calendar could.
///
/// <para>There were three. <b>Nights</b> (one day per row, hours across) was the
/// exact transpose of <see cref="History"/> once y became the time of day, and two
/// views of the same two axes is a thing to learn twice rather than a second question
/// answered.</para>
/// </summary>
public enum SkyLens
{
    /// <summary>Across is the date, down is the time of day. The home view.
    /// <i>When did things happen, and when in the day?</i></summary>
    History,

    /// <summary>A ring: time folded at a period the owner dials, so the history lies
    /// over itself. <i>Does it come round again?</i></summary>
    Cycle
}

/// <summary>One placed star, in content coordinates.</summary>
public readonly record struct SkyStar(double X, double Y);

public static class ConstellationLayout
{
    // ── The frame ─────────────────────────────────────────────────────────────
    /// <summary>The gutter down the left where the hours are written. The plot is
    /// shifted past it in SCREEN space, so the labels stay put while the dates scroll
    /// underneath: a gutter measured in world units would scroll away with them.</summary>
    public const double HourGutter = 34.0;

    /// <summary>Kept clear at the top for midnight's label, and at the foot for the
    /// dates.</summary>
    public const double TopPadding = 13.0;
    public const double BottomPadding = 19.0;

    // ── Star size ─────────────────────────────────────────────────────────────
    // One size for every category, always. Sizing a seizure differently from a dose
    // would be the app ranking them, and the legend would stop being a promise. What
    // varies is only how much room there is, and only inside a tight band, because a
    // symbol that changes size between screens is a symbol you have to re-learn.
    private const double MinRadius = 3.3;
    private const double MaxRadius = 5.0;

    /// <summary>
    /// A stable wobble so two entries at the very same minute are two marks rather than
    /// one: <b>along the date axis only</b>.
    ///
    /// <para>The height is left exact on purpose. A twice-daily dose draws two clean
    /// horizontal tracks across the sky, and anything scattered against them reads at a
    /// glance; a couple of pixels of vertical noise turns those tracks into fuzzy bands
    /// and throws the clearest signal on the whole surface away. The date axis has
    /// hundreds of pixels per day to hide a wobble in: an hour has a few.</para>
    /// </summary>
    private const double JitterAmplitude = 2.2;

    // ── Arriving ──────────────────────────────────────────────────────────────
    /// <summary>How much of the load is spent staggering rather than fading. 0.55 means
    /// the last entry starts arriving when the first is 55% through: weighted towards
    /// the sweep rather than the fade, because the travel is the part anyone sees.</summary>
    public const double StaggerShare = 0.55;

    /// <summary>
    /// How far into its own arrival a star is, given how far the load has run and where
    /// the star sits along the date axis (0 = the oldest edge, 1 = the newest).
    ///
    /// <para>The stagger runs along the DATE axis on purpose: the history visibly
    /// writes itself left to right, so the horizontal axis explains itself before
    /// anyone has read the caption. That is the whole reason the animation exists,
    /// it is the only ornament here that teaches something.</para>
    /// </summary>
    public static double ArrivalOf(double reveal, double xFraction)
    {
        if (reveal >= 1)
            return 1;

        var start = StaggerShare * Math.Clamp(xFraction, 0, 1);
        return Math.Clamp((reveal - start) / (1 - StaggerShare), 0, 1);
    }

    /// <summary>How big a star is drawn, from how much room each one has on screen.
    /// Derived from spacing rather than count, so zooming in grows the symbols back as
    /// the crowd around them thins: the progressive reveal, with no second rule.</summary>
    public static double StarRadius(double contentWidth, int count)
    {
        if (count <= 0 || contentWidth <= 0)
            return MaxRadius;

        return Math.Clamp(contentWidth / count * 0.7, MinRadius, MaxRadius);
    }

    /// <summary>Content x for a moment. The single place a date becomes a distance,
    /// linear, so equal gaps in time are equal gaps on screen at every zoom.</summary>
    public static double XFor(DateTime when, DateTime from, DateTime to, double contentWidth)
    {
        var span = (to - from).Ticks;
        if (span <= 0)
            return contentWidth / 2.0;

        return (when - from).Ticks / (double)span * contentWidth;
    }

    /// <summary>Canvas y for a time of day (0 = midnight, 1 = the next midnight).
    /// Midnight at the top, so the small hours are the top and bottom edges and the
    /// middle of the picture is the middle of the day.</summary>
    public static double HourY(double dayFraction, double contentHeight)
    {
        var top = TopPadding;
        var bottom = contentHeight - BottomPadding;
        return top + Math.Clamp(dayFraction, 0, 1) * Math.Max(0, bottom - top);
    }

    /// <summary>
    /// <b>The History lens.</b> x is the date, y is the time of day.
    /// </summary>
    /// <returns>One star per event, in the SAME order as <paramref name="events"/>, so
    /// index is identity: the detail sheet, the hit test and the drawing all key off
    /// it.</returns>
    public static SkyStar[] PlaceOnGrid(
        IReadOnlyList<CelestialEvent> events,
        DateTime from,
        DateTime to,
        double contentWidth,
        double contentHeight)
    {
        if (events.Count == 0 || contentWidth <= 0 || contentHeight <= 0)
            return Array.Empty<SkyStar>();

        var stars = new SkyStar[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var when = events[i].When;

            stars[i] = new SkyStar(
                XFor(when, from, to, contentWidth) + Jitter(when.Ticks, JitterAmplitude),
                HourY(when.TimeOfDay.Ticks / (double)TimeSpan.TicksPerDay, contentHeight));
        }

        return stars;
    }

    // ── The ring ──────────────────────────────────────────────────────────────
    /// <summary>Kept clear around the dial so no star is cut by the card's corner.</summary>
    private const double RingPadding = 30.0;

    /// <summary>The hole in the middle. Without it every entry from the first day of
    /// the range would land on one point, making the oldest the least legible.</summary>
    public const double RingInnerFraction = 0.24;

    /// <summary>
    /// <b>The Cycle lens.</b> Time folded at <paramref name="periodDays"/> and laid on
    /// a ring: the angle is where in the period a thing happened, the distance from
    /// the centre is how far through the chosen stretch it was: <b>oldest in the
    /// middle, most recent at the rim</b>, which the drawing labels, because an
    /// unexplained radius is a question rather than an answer.
    ///
    /// <para><b>The app never chooses the period.</b> It draws whatever fold it is
    /// handed and says nothing about the result, no "cycle detected", no highlight,
    /// no number. An alignment is something the person looking sees, not something
    /// Felova claims, and that distinction is the whole reason this may exist.</para>
    ///
    /// <para><b>A ring rather than a line, because a line has a seam.</b> Folded onto
    /// one, phase 0.98 and phase 0.02 land at opposite edges: minutes apart in cycle
    /// terms, drawn as two unrelated clumps, so the one arrangement built to reveal
    /// repeats could hide one purely because of where the range began.</para>
    /// </summary>
    /// <param name="periodDays">The fold. 1 is a day (a clock face), 7 a week.</param>
    public static SkyStar[] PlaceOnRing(
        IReadOnlyList<CelestialEvent> events,
        DateTime from,
        DateTime to,
        double periodDays,
        double width,
        double height)
    {
        if (events.Count == 0 || width <= 0 || height <= 0 || periodDays <= 0)
            return Array.Empty<SkyStar>();

        var centreX = width / 2;
        var centreY = height / 2;
        var outer = RingOuter(width, height);
        if (outer <= 0)
            return Array.Empty<SkyStar>();

        var inner = outer * RingInnerFraction;
        var span = (to - from).Ticks;

        var stars = new SkyStar[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            var when = events[i].When;

            // The start of the period at the top, running clockwise, like a clock.
            var phase = (when - from).TotalDays / periodDays;
            phase -= Math.Floor(phase);
            var angle = phase * Math.Tau - Math.PI / 2;

            var through = span <= 0 ? 1 : Math.Clamp((when - from).Ticks / (double)span, 0, 1);
            var radius = inner + through * (outer - inner);

            var wobble = Jitter(when.Ticks, 1.0);
            angle += wobble * 0.01;
            radius += wobble * 1.4;

            stars[i] = new SkyStar(
                centreX + Math.Cos(angle) * radius,
                centreY + Math.Sin(angle) * radius);
        }

        return stars;
    }

    /// <summary>The ring's outer edge: shared by the placement and the drawing, so
    /// the stars and the dial they sit on can never disagree.</summary>
    public static double RingOuter(double width, double height) =>
        Math.Min(width, height) / 2 - RingPadding;

    /// <summary>
    /// The star nearest a point, or -1 when the tap landed on empty sky. Nearest rather
    /// than first-hit so overlapping stars resolve to the one under the fingertip.
    /// </summary>
    /// <param name="reach">How near counts as a hit, in SCREEN units.</param>
    /// <param name="zoom">So the reach means the same distance on screen at every
    /// magnification: without it a zoomed-in tap would have to land within a fraction
    /// of a world unit, and a zoomed-out one would sweep up half a week.</param>
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
    /// stars swim on every invalidate, and this surface is looked at, not scrolled
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
