namespace Animal_Diary_App.Data.Services.Journal;

// ─────────────────────────────────────────────────────────────────────────────
//  The pet's own constellation.
//
//  A small figure of joined stars, drawn from the pet's NAME, sitting behind the
//  timeline. Charly's sky has Charly's shape in it and nobody else's, at every
//  range, from the day the app is installed.
//
//  It exists for three reasons, and the third is the important one:
//
//   1. The page is called a Constellation and, until this, wasn't one — it was a
//      scatter of symbols on a dark ground. This is the metaphor actually delivered.
//   2. Two pets in one household should not have interchangeable skies.
//   3. IT IS THERE ON DAY ONE. A new owner with three entries has almost nothing to
//      look at, and "almost nothing" is the screenshot they would have shared. The
//      figure is theirs before they have logged anything, and it never changes with
//      how much they log — so it can never read as a reward for logging more, or as
//      an emptiness for logging less.
//
//  ── The line this must not cross ──
//  It is DECORATION and encodes nothing. It is not placed on the timeline, it never
//  uses a category symbol, it is drawn dimmer than every event, and it does not move
//  when the sky is panned or zoomed. Nothing about the animal's health may ever reach
//  it — the moment the figure responded to what was recorded, the app would be
//  drawing a verdict in the sky.
//
//  MAUI-free, like ConstellationLayout, and tested for the same reason: "the same
//  name always draws the same figure" is a promise, not an implementation detail.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One star of the figure. Coordinates are 0..1 of the sky, mapped to the
/// canvas when drawn.</summary>
/// <param name="Brightness">0..1. A couple of anchor stars carry the shape; the rest
/// are quieter, which is what stops the figure reading as a flat dotted line.</param>
public readonly record struct AsterismStar(double X, double Y, double Brightness);

/// <summary>A join between two of the figure's stars, by index.</summary>
public readonly record struct AsterismLine(int From, int To);

/// <summary>One pet's figure: the stars and the lines that make it a shape.</summary>
public sealed class Asterism
{
    public static readonly Asterism Empty = new(Array.Empty<AsterismStar>(), Array.Empty<AsterismLine>());

    public Asterism(IReadOnlyList<AsterismStar> stars, IReadOnlyList<AsterismLine> lines)
    {
        Stars = stars;
        Lines = lines;
    }

    public IReadOnlyList<AsterismStar> Stars { get; }
    public IReadOnlyList<AsterismLine> Lines { get; }
    public bool HasShape => Stars.Count > 0;
}

public static class ConstellationAsterism
{
    private const int MinStars = 6;
    private const int MaxStars = 9;

    /// <summary>Kept clear of the frame, so no star of the figure is ever cut in half
    /// by the card's rounded corner.</summary>
    private const double Margin = 0.07;

    /// <summary>The band down the middle the figure stays out of — where the timeline
    /// and its events live. Not a hard guarantee (the path wanders wider than this at
    /// its extremes), but enough that the figure reads as behind the data rather than
    /// tangled in it.</summary>
    private const double Corridor = 0.15;

    private const double MinStep = 0.10;
    private const double MaxStep = 0.21;

    /// <summary>
    /// How much of the sky a figure should span, and the most it may be grown to get
    /// there. See <see cref="Fill"/>.
    ///
    /// <para>Deliberately under half. Spread across the whole card, the figure's joins
    /// became long lines wandering behind everything and its stars read as loose
    /// debris among the events rather than as one shape — the eye found the wreckage
    /// before it found the figure. A constellation is something you notice second.</para>
    /// </summary>
    private const double TargetExtent = 0.42;
    private const double MaxGrowth = 1.6;

    /// <summary>
    /// The figure for a pet. Same name, same figure, forever and on every device.
    /// </summary>
    /// <param name="name">The pet's name. Normalised (trimmed, case-folded) so
    /// "Charly" and "charly" are one animal, not two.</param>
    /// <param name="birthYear">Mixed into the seed purely for entropy — two dogs
    /// called Bella get different figures. It is never read back out, and nothing
    /// about the figure says anything about an age.</param>
    public static Asterism For(string? name, int birthYear = 0)
    {
        if ((name ?? string.Empty).Trim().Length == 0)
            return Asterism.Empty;

        return For(SkySignature.For(name, birthYear));
    }

    /// <summary>The figure for a pet's signature — the same seed that shapes their
    /// timeline and colours their atmosphere, so one pet's sky is one thing rather
    /// than three unrelated coincidences.</summary>
    public static Asterism For(in SkySignature signature)
    {
        // Decorrelated from the signature's own draws: sharing a seed should mean "the
        // same pet", not "the wave's phase and the first star's angle are the same
        // number". Without this, every figure would start at an angle tied to where
        // its timeline happened to enter the card.
        var rng = new SkySignature.SeedWalk(signature.Seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);
        var count = MinStars + rng.Next(MaxStars - MinStars + 1);

        var stars = new List<AsterismStar>(count);

        // A walk, not a scatter: each star steps away from the last at an angle that
        // turns but never doubles back. That is what makes the result read as a FIGURE
        // — a plough, a chair, a bird — instead of as confetti, and it is the whole
        // difference between this looking drawn and looking random.
        var x = Margin + rng.NextDouble() * 0.45;
        var y = Margin + rng.NextDouble() * (1 - 2 * Margin);
        var angle = rng.NextDouble() * Math.Tau;

        for (int i = 0; i < count; i++)
        {
            // The corridor push is applied at the very end, not here — Fill needs the
            // walk's honest shape to measure.
            stars.Add(new AsterismStar(x, y, 0.5 + rng.NextDouble() * 0.5));

            if (i == count - 1)
                break;

            var step = MinStep + rng.NextDouble() * (MaxStep - MinStep);

            // Up to a few tries to land inside the frame; the turn is at least ~34° so
            // three stars never fall in a dull straight line, and at most ~126° so the
            // walk never folds back over itself.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var turn = (0.6 + rng.NextDouble() * 1.6) * (rng.Next(2) == 0 ? 1 : -1);
                var candidate = angle + turn;
                var nx = x + Math.Cos(candidate) * step;
                var ny = y + Math.Sin(candidate) * step;

                if (nx is >= Margin and <= 1 - Margin && ny is >= Margin and <= 1 - Margin)
                {
                    angle = candidate;
                    x = nx;
                    y = ny;
                    break;
                }

                // Cornered: aim back towards the middle and try again.
                if (attempt == 5)
                {
                    angle = Math.Atan2(0.5 - y, 0.5 - x);
                    x = Math.Clamp(x + Math.Cos(angle) * step, Margin, 1 - Margin);
                    y = Math.Clamp(y + Math.Sin(angle) * step, Margin, 1 - Margin);
                }
            }
        }

        Fill(stars);
        Settle(stars, ref rng);

        var lines = new List<AsterismLine>(count);
        for (int i = 1; i < count; i++)
            lines.Add(new AsterismLine(i - 1, i));

        // One shortcut across the chain, closing a triangle somewhere along it. Real
        // asterisms fork; a pure chain reads as a worm.
        if (count >= MinStars)
        {
            var from = rng.Next(count - 2);
            lines.Add(new AsterismLine(from, from + 2));
        }

        return new Asterism(stars, lines);
    }

    /// <summary>
    /// Grow a small walk up towards a common size and settle it in the frame, then push
    /// it clear of the timeline's corridor.
    ///
    /// <para>Without this, a short walk landed as a thumbnail in one corner and read as
    /// incidental — a smudge that happened to be there — while a long one filled the
    /// sky. This is the pet's emblem and it appears in things people share, so every
    /// one of them should carry the same visual weight. The scale is CAPPED, so a
    /// figure is only ever grown towards that weight, never stretched to fill: the
    /// shapes stay the shapes the walk drew.</para>
    /// </summary>
    private static void Fill(List<AsterismStar> stars)
    {
        if (stars.Count == 0)
            return;

        double minX = 1, maxX = 0, minY = 1, maxY = 0;
        foreach (var star in stars)
        {
            minX = Math.Min(minX, star.X);
            maxX = Math.Max(maxX, star.X);
            minY = Math.Min(minY, star.Y);
            maxY = Math.Max(maxY, star.Y);
        }

        var extent = Math.Max(maxX - minX, maxY - minY);
        var scale = extent <= 0.001 ? 1 : Math.Clamp(TargetExtent / extent, 1.0, MaxGrowth);

        // Grow about the figure's own centre, then slide the whole thing back inside
        // the frame — scaling about the canvas centre instead would drag every figure
        // towards the middle and lose where the walk chose to sit.
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;

        var shiftX = Nudge(cx, (maxX - minX) * scale);
        var shiftY = Nudge(cy, (maxY - minY) * scale);

        for (int i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            stars[i] = new AsterismStar(
                Math.Clamp(cx + (star.X - cx) * scale + shiftX, Margin, 1 - Margin),
                Push(Math.Clamp(cy + (star.Y - cy) * scale + shiftY, Margin, 1 - Margin)),
                star.Brightness);
        }
    }

    /// <summary>
    /// Move the finished figure into ONE quarter of the sky, above the timeline or
    /// below it.
    ///
    /// <para>A small figure left wherever the walk happened to wander was as likely to
    /// sit across the middle as anywhere, tangled in the events. Choosing a corner for
    /// it — from the same seed, so it is still this pet's — puts it somewhere the eye
    /// can take it in as a shape, and leaves the timeline's own band clear.</para>
    /// </summary>
    private static void Settle(List<AsterismStar> stars, ref SkySignature.SeedWalk rng)
    {
        if (stars.Count == 0)
            return;

        double minX = 1, maxX = 0, minY = 1, maxY = 0;
        foreach (var star in stars)
        {
            minX = Math.Min(minX, star.X);
            maxX = Math.Max(maxX, star.X);
            minY = Math.Min(minY, star.Y);
            maxY = Math.Max(maxY, star.Y);
        }

        var width = maxX - minX;
        var height = maxY - minY;

        // Somewhere in the chosen half, with enough room left for the figure itself.
        var left = rng.Next(2) == 0;
        var above = rng.Next(2) == 0;

        var targetX = left
            ? Margin + rng.NextDouble() * Math.Max(0, 0.46 - width - Margin)
            : 1 - Margin - width - rng.NextDouble() * Math.Max(0, 0.46 - width - Margin);

        var bandDepth = Math.Max(0, 0.5 - Corridor - Margin - height);
        var targetY = above
            ? Margin + rng.NextDouble() * bandDepth
            : 1 - Margin - height - rng.NextDouble() * bandDepth;

        var shiftX = targetX - minX;
        var shiftY = targetY - minY;

        for (int i = 0; i < stars.Count; i++)
        {
            var star = stars[i];
            stars[i] = new AsterismStar(
                Math.Clamp(star.X + shiftX, Margin, 1 - Margin),
                Push(Math.Clamp(star.Y + shiftY, Margin, 1 - Margin)),
                star.Brightness);
        }
    }

    /// <summary>How far a figure of this width, centred here, has to move to sit inside
    /// the margins.</summary>
    private static double Nudge(double centre, double size)
    {
        var half = size / 2;
        if (centre - half < Margin)
            return Margin + half - centre;
        if (centre + half > 1 - Margin)
            return 1 - Margin - half - centre;
        return 0;
    }

    /// <summary>Nudge a star out of the corridor the timeline runs through, keeping the
    /// side it was already on.</summary>
    private static double Push(double y)
    {
        var offset = y - 0.5;
        if (Math.Abs(offset) >= Corridor)
            return y;

        return offset >= 0 ? 0.5 + Corridor : 0.5 - Corridor;
    }

}
