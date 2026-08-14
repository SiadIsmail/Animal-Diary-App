namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
//  The sky.
//
//  Pure Microsoft.Maui.Graphics, like every other chart in this app — no charting
//  package, because nothing on the drawing path may drag in a native library
//  (AI/known-constraints.md, and the two times this repo has already paid for it).
//
//  It draws four things and nothing else:
//    • an atmosphere (the rockpool wash after dark, its glows, and the app's own
//      drifting bubbles) that carries NO data whatsoever,
//    • a thin meandering line whose shape comes from x alone,
//    • one symbol per recorded event at its own moment in time,
//    • a date, occasionally, so the one real axis can be read.
//
//  There is no y-axis, no gridline, no bar, no aggregate and no trend, and there
//  must never be. Height on this canvas means "there were several at once", never
//  "it was worse".
//
//  It is DARK, and that is the only thing here that departs from the rest of the
//  app. Everything else is deliberately borrowed: the same vertical wash, the same
//  mint and warm-sand glows, and the same glass/outline/tint bubbles that float
//  behind every other page (WaterBackground). Without them this read as a different
//  product wearing Felova's data.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A label worth drawing, and where. Built outside this class so the formatting stays
/// localized and this one stays a painter.
/// </summary>
/// <param name="X">On the Timeline lens this is a WORLD x and the camera applies to
/// it. On the Clock and Rhythm lenses it is a canvas position, because neither of
/// those pans.</param>
/// <param name="Y">Canvas y. The timeline's dates all sit at the foot of the card;
/// the dial's hours sit around it.</param>
/// <param name="Pinned">Stays put while the content scrolls under it. The wall of
/// nights scrolls vertically, and its hours belong to the card while its dates belong
/// to the rows — so the two have to behave differently.</param>
public readonly record struct SkyTick(double X, double Y, string Label, bool Pinned = false);

public sealed class ConstellationDrawable : IDrawable
{
    // ── What to draw (assigned by the page; never mutated here) ──────────────────
    public IReadOnlyList<CelestialEvent> Events { get; set; } = Array.Empty<CelestialEvent>();
    public IReadOnlyList<SkyStar> Stars { get; set; } = Array.Empty<SkyStar>();
    public IReadOnlyList<SkyTick> Ticks { get; set; } = Array.Empty<SkyTick>();

    /// <summary>The pet's own figure, behind everything. Fixed to the CANVAS, not to
    /// time — it does not scroll and it does not zoom, because it is whose sky this is
    /// rather than something that happened in it.</summary>
    public Asterism Asterism { get; set; } = Asterism.Empty;

    /// <summary>
    /// Everything decorative that belongs to this pet: the wave's shape, the seed the
    /// starfield generates itself from, and the one colour the atmosphere leans
    /// towards. Setting it re-mixes the palette, which is why it is a property with a
    /// body rather than a field.
    /// </summary>
    public SkySignature Signature
    {
        get => _signature;
        set
        {
            _signature = value;
            MixPalette();
        }
    }

    private SkySignature _signature = SkySignature.Default;

    // ── The camera ───────────────────────────────────────────────────────────────
    // Stars are placed ONCE, in world units, and this is the lens they are looked at
    // through:  screenX = worldX × Zoom − ScrollX.
    //
    // That the layout does not move is the whole of what makes this a zoom rather
    // than a re-draw. The first version re-placed everything at each zoom step, so
    // the wave reshaped and crowded stars jumped to different rings on the way in —
    // the picture kept becoming a DIFFERENT picture, which is why it read as
    // confusing rather than as magnification. A constellation has to keep its shape.
    //
    // Only time zooms. Y is untouched, because time is the only axis that means
    // anything: pulling two entries apart along it is exactly what "reveal more
    // individual events" is, and stretching the sky vertically would only push the
    // constellation off its own canvas.

    /// <summary>How far the sky has been dragged, in SCREEN units.</summary>
    public double ScrollX { get; set; }

    /// <summary>Magnification along time. 1 = the whole chosen stretch fits.</summary>
    public double Zoom { get; set; } = 1;

    /// <summary>The stretch's width in WORLD units (the viewport width at zoom 1).
    /// Star size is read from it, so it has to be set alongside <see cref="Stars"/>.</summary>
    public double WorldWidth { get; set; }

    /// <summary>The tapped star, or -1. The only thing on this canvas that is
    /// emphasised, and it is emphasised because a finger chose it — never because of
    /// what it holds.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>Which way the sky is folded. The Timeline hangs its stars off a
    /// horizon and moves under the camera; the Clock is a dial and does neither.</summary>
    public SkyLens Lens { get; set; } = SkyLens.Timeline;

    /// <summary>How far the wall of nights has been dragged, in canvas units. The
    /// Nights lens is the only one that scrolls vertically — a year of rows is taller
    /// than any card.</summary>
    public double ScrollY { get; set; }

    /// <summary>One night's height on the wall, from <c>ConstellationLayout.RowHeight</c>.</summary>
    public double RowHeight { get; set; }

    /// <summary>How many nights the wall holds.</summary>
    public int RowCount { get; set; }

    // ── The lens change, in flight ───────────────────────────────────────────────
    // Switching lens used to be a cut: four screens with a hard edge between them.
    // Flown instead, the transformation TEACHES ITSELF — three months of entries
    // visibly collapse into a wedge at three in the morning, and nobody has to read a
    // sentence to understand what the Clock is. It is the difference between four
    // views and one instrument.
    //
    // The tween runs in SCREEN space, deliberately. Each lens has its own camera (the
    // Timeline zooms, the wall scrolls) and interpolating between two cameras as well
    // as two layouts is a knot with nothing to show for it; resolving both ends to
    // pixels first makes the whole thing one lerp.

    /// <summary>0 → the old lens, 1 → settled on the new one. 1 at rest.</summary>
    public double Transition { get; set; } = 1;

    /// <summary>The lens being left. Its backdrop fades out as the new one fades in.</summary>
    public SkyLens FromLens { get; set; } = SkyLens.Timeline;

    /// <summary>Where each star was, and where it is going, already in canvas
    /// coordinates. One entry per event in <see cref="Events"/> order, or empty when
    /// nothing is in flight.</summary>
    public IReadOnlyList<PointF> TweenFrom { get; set; } = Array.Empty<PointF>();
    public IReadOnlyList<PointF> TweenTo { get; set; } = Array.Empty<PointF>();

    /// <summary>Symbols are sized differently per lens (a wall bounds them by its
    /// rows), so the size flies too.</summary>
    public float TweenFromRadius { get; set; }
    public float TweenToRadius { get; set; }

    private bool InFlight => Transition < 1 && TweenFrom.Count > 0 && TweenFrom.Count == TweenTo.Count;

    /// <summary>
    /// One kind brought forward, everything else dropped back to context — or null for
    /// all eight at once.
    ///
    /// <para>Eight categories overlaid is busy by construction, and no amount of layout
    /// tuning fixes a legibility problem caused by showing everything. "Seizures, with
    /// the rest of life behind them" is a different picture, and it is the one someone
    /// actually came here to look at. Nothing is computed and nothing is hidden — the
    /// rest of the sky is still there, just quieter.</para>
    /// </summary>
    public CelestialCategory? Focus { get; set; }

    // ── Palette (resolved through AppColors, never hex literals) ─────────────────
    private readonly Color _skyTop = AppColors.Resolve("NightTop", Color.FromArgb("#123C3E"));
    private readonly Color _skyMid = AppColors.Resolve("NightMid", Color.FromArgb("#0B2A31"));
    private readonly Color _skyBottom = AppColors.Resolve("NightBottom", Color.FromArgb("#08191F"));
    private readonly Color _glowMint = AppColors.Resolve("NightGlowMint", Color.FromArgb("#8FE3D2"));
    private readonly Color _glowSand = AppColors.Resolve("NightGlowSand", Color.FromArgb("#EDDFC0"));
    private readonly Color _bubbleGlass = AppColors.Resolve("White", Colors.White);
    // WaterBackground's tinted bubbles are Teal (#149081) because they sit on a pale
    // mint page. That colour is DARKER than this ground, so it would be a hole rather
    // than a bubble — the night sibling of the same accent is the one to use.
    private readonly Color _bubbleTint = AppColors.Resolve("SkyPath", Color.FromArgb("#7FD4C4"));
    private readonly Color _dustBase = AppColors.Resolve("StarDust", Color.FromArgb("#C4E6E3"));
    private readonly Color _asterismBase = AppColors.Resolve("NightAsterism", Color.FromArgb("#D8EFE6"));
    private readonly Color _pathBase = AppColors.Resolve("SkyPath", Color.FromArgb("#7FD4C4"));
    private readonly Color _tickColor = AppColors.Resolve("SkyInk", Color.FromArgb("#9FC4C0"));

    private readonly Color[] _starColors = CelestialVisuals.All
        .Select(c => AppColors.Resolve(CelestialVisuals.For(c).ColorKey, Colors.White))
        .ToArray();

    // ── The pet's weather ────────────────────────────────────────────────────────
    // Mixed towards the signature's accent, never replaced by it: the mint timeline
    // and the pale dust belong to the app and only LEAN towards the pet's colour. A
    // full swap would give one owner a rose page and another a violet one, and this
    // would stop being one product. The eight Star* category colours are untouched by
    // any of it — a weigh-in is the same blue in everybody's sky, or the legend stops
    // being a promise.
    private Color _accent = AppColors.Resolve("SkyAccentTeal", Color.FromArgb("#6FBFB2"));
    private Color _glowTop;
    private Color _dust;
    private Color _asterism;
    private Color _pathColor;

    public ConstellationDrawable() => MixPalette();

    // The attribute is what tells the compiler the constructor's one call fills all
    // four; without it every mixed colour would have to carry a misleading initializer.
    [System.Diagnostics.CodeAnalysis.MemberNotNull(
        nameof(_glowTop), nameof(_dust), nameof(_asterism), nameof(_pathColor))]
    private void MixPalette()
    {
        _accent = AppColors.Resolve(_signature.AccentKey, Color.FromArgb("#6FBFB2"));

        // The glow up top is mostly the pet's — it is the layer with no job other than
        // atmosphere, so it can carry the most of them. The WARM SAND glow at the
        // bottom is never tinted: it is the app's own signature and the thing that
        // makes this a rockpool at night rather than a generic dark screen.
        _glowTop = Blend(_glowMint, _accent, 0.72f);
        _dust = Blend(_dustBase, _accent, 0.4f);
        _asterism = Blend(_asterismBase, _accent, 0.32f);
        _pathColor = Blend(_pathBase, _accent, 0.22f);
    }

    private static Color Blend(Color from, Color to, float amount) => new(
        from.Red + (to.Red - from.Red) * amount,
        from.Green + (to.Green - from.Green) * amount,
        from.Blue + (to.Blue - from.Blue) * amount);

    /// <summary>Below this radius a star's halo is dropped. Not a count: a halo is
    /// three fills and roughly three times the symbol's width, so in a crowded sky it
    /// is both the slow part AND the part that welds neighbours into one blob.</summary>
    private const float GlowRadiusFloor = 3.8f;

    /// <summary>
    /// World x → screen x. The one place the camera is applied; everything that draws
    /// goes through it so nothing can drift out of step.
    ///
    /// <para><b>Only the Timeline has a camera.</b> A clock is not panned and a fold is
    /// one turn wide by definition — on those lenses a star's x is already a canvas
    /// position, and this is the identity.</para>
    /// </summary>
    private float ScreenX(double worldX) =>
        Lens == SkyLens.Timeline ? (float)(worldX * Zoom - ScrollX) : (float)worldX;

    /// <summary>Screen x → world x, for the layers that are generated from the
    /// position they sit at rather than stored.</summary>
    private double WorldX(double screenX) =>
        Lens == SkyLens.Timeline ? (screenX + ScrollX) / (Zoom <= 0 ? 1 : Zoom) : screenX;

    /// <summary>Where a star is actually drawn: its moment, plus the sideways nudge
    /// that breaks a knot of entries into a cluster and fades away as the zoom starts
    /// separating them for real.</summary>
    private float StarX(in SkyStar star) =>
        ScreenX(star.X) + (float)(star.NudgeX * ConstellationLayout.NudgeFade(Zoom));

    /// <summary>Canvas y for a placed star. Only the wall of nights scrolls vertically;
    /// on every other lens a star's y is already where it belongs.</summary>
    private float ScreenY(double contentY) =>
        (float)(Lens == SkyLens.Nights ? contentY - ScrollY : contentY);

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var t = (float)Math.Clamp(Transition, 0, 1);

        DrawAtmosphere(canvas, rect);
        DrawBubbles(canvas, rect);
        DrawDust(canvas, rect);

        // The pet's figure sits behind three of the four lenses. Not the wall: a
        // constellation drawn across a hundred ruled rows is noise, and the rows are
        // the thing being read. Mid-flight it fades rather than blinks.
        var figure = Lerp(Weight(FromLens), Weight(Lens), t);
        if (figure > 0.01f)
            DrawAsterism(canvas, rect, figure);

        if (t < 1 && FromLens != Lens)
        {
            DrawBackdrop(canvas, rect, FromLens, 1 - t);
            DrawBackdrop(canvas, rect, Lens, t);
        }
        else
        {
            DrawBackdrop(canvas, rect, Lens, 1);
        }

        // The labels belong to the arriving lens; they fade up rather than travel,
        // because a date sliding into an hour means nothing.
        DrawTicks(canvas, rect, t);
        DrawStars(canvas, rect, t);
        DrawVignette(canvas, rect);

        static float Weight(SkyLens lens) => lens == SkyLens.Nights ? 0f : 1f;
    }

    private void DrawBackdrop(ICanvas canvas, RectF rect, SkyLens lens, float alpha)
    {
        switch (lens)
        {
            case SkyLens.Clock: DrawDial(canvas, rect, alpha); break;
            case SkyLens.Nights: DrawWall(canvas, rect, alpha); break;
            default: DrawPath(canvas, rect, alpha); break;
        }
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;

    /// <summary>A soft darkening at the corners. It is what makes the card read as a
    /// window onto a night rather than a rectangle painted dark — and it settles the
    /// edges, where the rounded corner would otherwise cut a bright star in half.</summary>
    private void DrawVignette(ICanvas canvas, RectF rect)
    {
        var radius = MathF.Max(rect.Width, rect.Height) * 0.78f;
        var bounds = new RectF(
            rect.Center.X - radius, rect.Center.Y - radius, radius * 2, radius * 2);

        canvas.SaveState();
        canvas.SetFillPaint(new RadialGradientPaint
        {
            StartColor = _skyBottom.WithAlpha(0f),
            EndColor = _skyBottom.WithAlpha(0.55f),
            Center = new Point(0.5, 0.5),
            Radius = 0.5
        }, bounds);
        canvas.FillRectangle(rect);
        canvas.RestoreState();
    }

    // ── Atmosphere: pure decoration, and deliberately so ─────────────────────────

    private void DrawAtmosphere(ICanvas canvas, RectF rect)
    {
        // The same three-stop vertical wash WaterBackground paints, after dark.
        //
        // EVERY gradient on this canvas is fenced inside Save/RestoreState. A paint set
        // with SetFillPaint is canvas state, and on Android it survives a later
        // `FillColor =` assignment — so one un-fenced gradient here left every FILLED
        // symbol being painted with a radial gradient centred somewhere off in the
        // atmosphere, which at the symbol's position had faded to fully transparent.
        // The result: every star vanished except the medication ring, the one symbol
        // drawn with a STROKE. The legend was unaffected because it paints no
        // gradients. Do not unwrap these.
        canvas.SaveState();
        canvas.SetFillPaint(new LinearGradientPaint
        {
            GradientStops = new[]
            {
                new PaintGradientStop(0f, _skyTop),
                new PaintGradientStop(0.52f, _skyMid),
                new PaintGradientStop(1f, _skyBottom),
            },
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        }, rect);
        canvas.FillRectangle(rect);
        canvas.RestoreState();

        // Mint glow off the top-left corner, and warm sand rising off the bottom edge —
        // the two glows that make the app's background warm rather than clinical. They
        // are anchored to the CANVAS, not to time: an atmosphere that scrolled would
        // start to look like it meant something.
        Glow(canvas, _glowTop, 0.22f,
            new PointF(rect.X + rect.Width * 0.16f, rect.Y + rect.Height * 0.08f),
            rect.Width * 0.62f);

        Glow(canvas, _glowSand, 0.15f,
            new PointF(rect.X + rect.Width * 0.62f, rect.Bottom + rect.Height * 0.12f),
            rect.Width * 0.75f);
    }

    private static void Glow(ICanvas canvas, Color color, float strength, PointF centre, float radius)
    {
        var bounds = new RectF(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);

        canvas.SaveState();
        canvas.SetFillPaint(new RadialGradientPaint
        {
            StartColor = color.WithAlpha(strength),
            EndColor = color.WithAlpha(0f),
            Center = new Point(0.5, 0.5),
            Radius = 0.5
        }, bounds);
        canvas.FillRectangle(bounds);
        canvas.RestoreState();
    }

    /// <summary>
    /// Felova's bubbles, drifting behind the stars. The three kinds are the ones
    /// <c>WaterBackground</c> already floats on every other page — glass (a soft
    /// white fill inside a white ring), outline (ring only) and tint (a teal ring) —
    /// at night strength.
    ///
    /// <para>Generated from the content x they sit at rather than stored, so the field
    /// is endless and identical on every redraw. They drift at a quarter of the scroll:
    /// slow enough to read as far away, and slow enough that nobody could mistake one
    /// for something that happened.</para>
    /// </summary>
    private void DrawBubbles(ICanvas canvas, RectF rect)
    {
        const float cell = 260f;
        const float parallax = 0.24f;

        var start = (float)(ScrollX * parallax);
        var firstCell = (int)MathF.Floor(start / cell) - 1;
        var lastCell = (int)MathF.Ceiling((start + rect.Width) / cell) + 1;

        canvas.StrokeSize = 1.5f;

        for (int c = firstCell; c <= lastCell; c++)
        {
            var h = Hash((uint)c * 2654435761u ^ (uint)_signature.Seed);

            var radius = 16f + (h & 0x3F) / 63f * 74f;
            var x = rect.X + c * cell + ((h >> 6) & 0x7F) / 127f * cell - start;
            var y = rect.Y + ((h >> 13) & 0xFF) / 255f * rect.Height;

            if (x + radius < rect.X || x - radius > rect.Right)
                continue;

            // Flat fills, no gradients: partly for the paint-state reason above, and
            // partly because a radial fade at 7% white on a dark ground was
            // arithmetically invisible — the first attempt at these drew nothing at
            // all. On this background a bubble has to be carried by its RING.
            switch ((h >> 21) % 3)
            {
                case 0: // glass — a faint wash inside its ring
                    canvas.FillColor = _bubbleGlass.WithAlpha(0.035f);
                    canvas.FillCircle(x, y, radius);
                    canvas.StrokeColor = _bubbleGlass.WithAlpha(0.16f);
                    break;

                case 1: // outline
                    canvas.StrokeColor = _bubbleGlass.WithAlpha(0.12f);
                    break;

                default: // tint
                    canvas.StrokeColor = _bubbleTint.WithAlpha(0.22f);
                    break;
            }

            canvas.DrawCircle(x, y, radius);
        }
    }

    /// <summary>Distant stars, behind everything. Same generated-from-x trick as the
    /// bubbles, drifting at a different rate so the two layers separate.</summary>
    private void DrawDust(ICanvas canvas, RectF rect)
    {
        const float cell = 44f;
        const float parallax = 0.45f;

        var start = (float)(ScrollX * parallax);
        var firstCell = (int)MathF.Floor(start / cell) - 1;
        var lastCell = (int)MathF.Ceiling((start + rect.Width) / cell) + 1;

        for (int c = firstCell; c <= lastCell; c++)
        {
            for (int n = 0; n < 2; n++)
            {
                var h = Hash((uint)(c * 2 + n) ^ (uint)(_signature.Seed >> 32));
                var x = rect.X + c * cell + (h & 0x3F) / 63f * cell - start;
                if (x < rect.X - 2 || x > rect.Right + 2)
                    continue;

                var y = rect.Y + ((h >> 6) & 0xFF) / 255f * rect.Height;
                var radius = 0.5f + ((h >> 14) & 0x3) * 0.28f;
                var alpha = 0.1f + ((h >> 18) & 0xF) / 15f * 0.22f;

                canvas.FillColor = _dust.WithAlpha(alpha);
                canvas.FillCircle(x, y, radius);
            }
        }
    }

    /// <summary>
    /// The pet's figure. Drawn between the ambient layers and the timeline, so it sits
    /// behind everything that was recorded.
    ///
    /// <para>Two rules hold it on the right side of the line between decoration and
    /// data: its stars are plain round points (never one of the eight symbols, which
    /// each mean something), and they are dimmer than any event. Someone glancing at
    /// this must never wonder whether the figure is telling them something.</para>
    /// </summary>
    private void DrawAsterism(ICanvas canvas, RectF rect, float alpha)
    {
        if (!Asterism.HasShape)
            return;

        var stars = Asterism.Stars;

        float X(int i) => rect.X + (float)stars[i].X * rect.Width;
        float Y(int i) => rect.Y + (float)stars[i].Y * rect.Height;

        // Dim. The figure is the room, not the record — if it can compete with an event
        // for attention, it is drawn wrong.
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = _asterism.WithAlpha(0.10f * alpha);
        canvas.StrokeLineCap = LineCap.Round;

        foreach (var line in Asterism.Lines)
        {
            if (line.From < 0 || line.To < 0 || line.From >= stars.Count || line.To >= stars.Count)
                continue;

            canvas.DrawLine(X(line.From), Y(line.From), X(line.To), Y(line.To));
        }

        for (int i = 0; i < stars.Count; i++)
        {
            var brightness = (float)stars[i].Brightness;
            var radius = 1.5f + brightness * 1.9f;

            canvas.FillColor = _asterism.WithAlpha(0.07f * brightness * alpha);
            canvas.FillCircle(X(i), Y(i), radius * 2.8f);
            canvas.FillColor = _asterism.WithAlpha((0.22f + 0.2f * brightness) * alpha);
            canvas.FillCircle(X(i), Y(i), radius);
        }
    }

    // ── The line ─────────────────────────────────────────────────────────────────

    /// <summary>A thin glowing path rather than an axis: two strokes, the wide one
    /// nearly transparent. Sampled every few pixels because it is a sine sum, not a
    /// data series — there are no points to join.</summary>
    private void DrawPath(ICanvas canvas, RectF rect, float alpha)
    {
        const float step = 5f;
        var path = new PathF();

        for (float sx = -step; sx <= rect.Width + step; sx += step)
        {
            // Sampled in WORLD x, so the wave is the same wave at every zoom — it just
            // stretches. This is what keeps a star on the line it was placed against
            // instead of the line drifting out from under it as you zoom.
            var y = (float)ConstellationLayout.PathY(WorldX(sx), rect.Height, _signature);
            if (sx <= -step)
                path.MoveTo(rect.X + sx, rect.Y + y);
            else
                path.LineTo(rect.X + sx, rect.Y + y);
        }

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        canvas.StrokeColor = _pathColor.WithAlpha(0.11f * alpha);
        canvas.StrokeSize = 7f;
        canvas.DrawPath(path);

        canvas.StrokeColor = _pathColor.WithAlpha(0.5f * alpha);
        canvas.StrokeSize = 1.2f;
        canvas.DrawPath(path);
    }

    /// <summary>
    /// The Clock's face: one thin ring, four quarter marks, and nothing else.
    ///
    /// <para>No hour hand, no numbers around the inside, no spokes. The dial's whole
    /// job is to say "this is a day, midnight is up" and then get out of the way — the
    /// wedge of stars is the thing being looked at, and every extra line drawn here is
    /// something competing with it.</para>
    /// </summary>
    private void DrawDial(ICanvas canvas, RectF rect, float alpha)
    {
        var centreX = rect.X + rect.Width / 2;
        var centreY = rect.Y + rect.Height / 2;
        var outer = MathF.Min(rect.Width, rect.Height) / 2 - 26f;
        if (outer <= 0)
            return;

        canvas.StrokeLineCap = LineCap.Round;

        canvas.StrokeColor = _pathColor.WithAlpha(0.10f * alpha);
        canvas.StrokeSize = 5f;
        canvas.DrawCircle(centreX, centreY, outer);

        canvas.StrokeColor = _pathColor.WithAlpha(0.4f * alpha);
        canvas.StrokeSize = 1f;
        canvas.DrawCircle(centreX, centreY, outer);

        // Midnight, six, noon, six — just enough to know which way round the day runs.
        canvas.StrokeColor = _pathColor.WithAlpha(0.3f * alpha);
        canvas.StrokeSize = 1.2f;
        for (int q = 0; q < 4; q++)
        {
            var angle = q / 4f * MathF.Tau - MathF.PI / 2f;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            canvas.DrawLine(
                centreX + cos * (outer - 6f), centreY + sin * (outer - 6f),
                centreX + cos * (outer + 6f), centreY + sin * (outer + 6f));
        }
    }

    /// <summary>
    /// The wall's own backdrop: a soft daylight wash across the middle of each day, and
    /// the faintest banding to tell one night from the next.
    ///
    /// <para><b>The wash is the whole idea.</b> With the daytime hours lit and the
    /// small hours left dark, "these keep happening in the middle of the night" is
    /// something you see before you have read a single label — and it is drawn from
    /// clock time alone, so it asserts nothing. It is a gradient rather than two hard
    /// edges because dawn is not a boundary and the picture should not pretend it
    /// is.</para>
    ///
    /// <para>The banding is deliberately at the edge of visible. Rows have to be
    /// separable or the wall is a smear, but this app does not draw gridlines, and a
    /// ruled page would turn the one surface built for looking into a spreadsheet.</para>
    /// </summary>
    private void DrawWall(ICanvas canvas, RectF rect, float alpha)
    {
        if (RowHeight <= 0 || RowCount <= 0)
            return;

        var inset = (float)ConstellationLayout.WallInset;
        var plotLeft = rect.X + inset;
        var plotWidth = rect.Width - inset - 8f;
        if (plotWidth <= 0)
            return;

        // Daylight: nothing at midnight, warmest around noon.
        var wash = new RectF(plotLeft, rect.Y, plotWidth, rect.Height);
        canvas.SaveState();
        canvas.SetFillPaint(new LinearGradientPaint
        {
            GradientStops = new[]
            {
                new PaintGradientStop(0f, _glowSand.WithAlpha(0f)),
                new PaintGradientStop(0.27f, _glowSand.WithAlpha(0.045f * alpha)),
                new PaintGradientStop(0.5f, _glowSand.WithAlpha(0.075f * alpha)),
                new PaintGradientStop(0.73f, _glowSand.WithAlpha(0.045f * alpha)),
                new PaintGradientStop(1f, _glowSand.WithAlpha(0f)),
            },
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        }, wash);
        canvas.FillRectangle(wash);
        canvas.RestoreState();

        // Only the rows actually on screen — a year is three thousand pixels of wall.
        var first = Math.Max(0, (int)Math.Floor(ScrollY / RowHeight));
        var last = Math.Min(RowCount - 1, (int)Math.Ceiling((ScrollY + rect.Height) / RowHeight));

        canvas.FillColor = _dust.WithAlpha(0.028f * alpha);
        for (int row = first; row <= last; row++)
        {
            if (row % 2 != 0)
                continue;

            var top = rect.Y + (float)(row * RowHeight - ScrollY);
            canvas.FillRectangle(plotLeft, top, plotWidth, (float)RowHeight);
        }
    }

    /// <summary>Dates, floated at the foot of the sky with no rule under them. The one
    /// axis that means anything is time, so it is the one that gets labels — and even
    /// it gets no line.</summary>
    private void DrawTicks(ICanvas canvas, RectF rect, float alpha)
    {
        if (Ticks.Count == 0 || alpha <= 0.01f)
            return;

        canvas.Font = Font.Default;
        canvas.FontSize = 10f;
        canvas.FontColor = _tickColor.WithAlpha(0.55f * alpha);

        foreach (var tick in Ticks)
        {
            // Only the Timeline has a camera; the dial and the fold are pinned to the
            // canvas, so their labels are already where they belong.
            var x = rect.X + (Lens == SkyLens.Timeline ? ScreenX(tick.X) : (float)tick.X);
            if (x < rect.X - 40 || x > rect.Right + 40)
                continue;

            var y = rect.Y + (tick.Pinned ? (float)tick.Y : ScreenY(tick.Y));
            if (y < rect.Y - 12 || y > rect.Bottom + 12)
                continue;

            canvas.DrawString(tick.Label, x, y, HorizontalAlignment.Center);
        }
    }

    // ── The events ───────────────────────────────────────────────────────────────

    private void DrawStars(ICanvas canvas, RectF rect, float t)
    {
        if (InFlight)
        {
            DrawStarsInFlight(canvas, rect, t);
            return;
        }

        if (Stars.Count == 0)
            return;

        // Size comes from how much room each star has ON SCREEN — the world's width
        // times the magnification. So zooming in genuinely grows the symbols back to
        // full size as the crowd around them thins out: the reveal is one rule, not a
        // second code path (see ConstellationLayout.StarRadius).
        var screenWidth = (WorldWidth > 0 ? WorldWidth : rect.Width) * (Zoom <= 0 ? 1 : Zoom);

        // On the wall a symbol is bounded by its ROW, not by how much time is on
        // screen: rows are the structure, and a star that overflowed one would be read
        // as belonging to the night above or below it.
        var radius = Lens == SkyLens.Nights
            ? (float)Math.Clamp(RowHeight * 0.38, 2.2, 5.6)
            : (float)ConstellationLayout.StarRadius(screenWidth, Events.Count);

        // Glow needs ROOM, not just size. A halo is wider than its symbol, so on a run
        // of daily entries — one mood a day, ten pixels apart — every halo touched its
        // neighbours' and fifteen stars welded into one fuzzy caterpillar with no
        // symbol left in it. Where the sky is that busy, the stars go bare.
        var spacing = Events.Count > 0 ? screenWidth / Events.Count : double.MaxValue;
        var glow = radius >= GlowRadiusFloor && spacing > radius * 3.0;

        var margin = radius * 4f + 8f;

        if (Focus is null)
            DrawLinks(canvas, rect, margin);

        // Two passes when one kind is in focus, so the quiet ones are laid down first
        // and the focused kind sits on top of them rather than being buried by whatever
        // happened to be logged later.
        var passes = Focus is null ? 1 : 2;
        for (int pass = 0; pass < passes; pass++)
        for (int i = 0; i < Stars.Count; i++)
        {
            var star = Stars[i];
            var x = StarX(star);
            if (x < -margin || x > rect.Width + margin)
                continue;

            if (Focus is CelestialCategory focused && i < Events.Count)
            {
                var inFocus = Events[i].Category == focused;
                if (inFocus != (pass == 1))
                    continue;
            }

            var selected = i == SelectedIndex;
            // Stars and events are placed one-for-one and in the same order; the guard
            // is for the frame between a new load and its re-place, not a real state.
            var category = i < Events.Count ? Events[i].Category : CelestialCategory.Custom;
            var color = ColorFor(category);

            // Out of focus: still there, still in its own colour and its own shape,
            // just quieter. Dropped rather than hidden, because the whole value of the
            // focused kind is seeing it against everything else that was going on.
            var dimmed = Focus is CelestialCategory kind && kind != category;
            if (dimmed)
                color = color.WithAlpha(0.3f);

            CelestialSymbols.Draw(
                canvas,
                category,
                rect.X + x,
                rect.Y + ScreenY(star.Y),
                selected ? MathF.Max(radius * 1.6f, 6f) : (dimmed ? radius * 0.72f : radius),
                color,
                (glow || selected) && !dimmed,
                // The alternating hand-made tilt every icon tile in this app wears
                // (TimelineItem.IconRotation), in radians.
                tilt: i % 2 == 0 ? -0.11f : 0.09f);

            if (selected)
            {
                canvas.StrokeColor = color.WithAlpha(0.75f);
                canvas.StrokeSize = 1.2f;
                canvas.DrawCircle(rect.X + x, rect.Y + ScreenY(star.Y), MathF.Max(radius * 3.4f, 15f));
            }
        }
    }

    /// <summary>
    /// Mid-flight: every star drawn between where it was and where it is going.
    ///
    /// <para>No links, no guides, no selection ring — the only thing worth watching
    /// during a lens change is the entries themselves moving, and everything else is
    /// furniture that belongs to one end or the other. Positions are already in canvas
    /// coordinates, so the camera is deliberately not applied: it belongs to whichever
    /// lens the flight lands on.</para>
    /// </summary>
    private void DrawStarsInFlight(ICanvas canvas, RectF rect, float t)
    {
        var radius = Lerp(TweenFromRadius, TweenToRadius, t);
        if (radius <= 0)
            return;

        for (int i = 0; i < TweenFrom.Count && i < Events.Count; i++)
        {
            var from = TweenFrom[i];
            var to = TweenTo[i];

            var x = rect.X + Lerp(from.X, to.X, t);
            var y = rect.Y + Lerp(from.Y, to.Y, t);
            if (x < rect.X - 40 || x > rect.Right + 40 || y < rect.Y - 40 || y > rect.Bottom + 40)
                continue;

            var category = Events[i].Category;
            var color = ColorFor(category);
            if (Focus is CelestialCategory kind && kind != category)
                color = color.WithAlpha(0.3f);

            CelestialSymbols.Draw(canvas, category, x, y, radius, color, glow: false,
                tilt: i % 2 == 0 ? -0.11f : 0.09f);
        }
    }

    /// <summary>
    /// The hairlines that make this a constellation rather than a scatter: each star
    /// joined to the one beside it in the SAME MOMENT (see <c>SkyStar.LinkTo</c>).
    ///
    /// <para>Drawn only when the two are between <c>LinkMinimum</c> and
    /// <c>LinkMaximum</c> apart on screen — which is what makes them a reward
    /// for zooming in. Too close together and the line is a smudge inside a blob; too
    /// far apart and a year's worth of them becomes a scribble across the sky. In
    /// between, a morning's glucose curve draws itself.</para>
    /// </summary>
    private void DrawLinks(ICanvas canvas, RectF rect, float margin)
    {
        const float LinkMinimum = 5f;
        const float LinkMaximum = 78f;

        canvas.StrokeSize = 0.9f;
        canvas.StrokeColor = _dust.WithAlpha(0.16f);

        for (int i = 0; i < Stars.Count; i++)
        {
            var star = Stars[i];
            var link = star.LinkTo;
            if (link < 0 || link >= Stars.Count)
                continue;

            var x = StarX(star);
            if (x < -margin || x > rect.Width + margin)
                continue;

            var other = Stars[link];
            var ox = StarX(other);

            var dx = x - ox;
            var dy = (float)(star.Y - other.Y);
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance < LinkMinimum || distance > LinkMaximum)
                continue;

            canvas.DrawLine(
                rect.X + ox, rect.Y + ScreenY(other.Y),
                rect.X + x, rect.Y + ScreenY(star.Y));
        }
    }

    private Color ColorFor(CelestialCategory category)
    {
        var i = (int)category;
        return i >= 0 && i < _starColors.Length ? _starColors[i] : _starColors[^1];
    }

    /// <summary>A cheap stable scramble for the ambient layers — same cell, same
    /// bubble, forever. (Finalizer of the xorshift/multiply family used by
    /// <c>ConstellationLayout</c>'s jitter, at 32 bits.)</summary>
    private static uint Hash(uint value)
    {
        unchecked
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }
    }
}
