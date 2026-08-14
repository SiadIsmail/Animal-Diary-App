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

/// <summary>A date worth naming, at a content x. Built by the ViewModel so the
/// formatting stays localized and this class stays a painter.</summary>
public readonly record struct SkyTick(double X, string Label);

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

    /// <summary>World x → screen x. The one place the camera is applied; everything
    /// that draws goes through it so nothing can drift out of step.</summary>
    private float ScreenX(double worldX) => (float)(worldX * Zoom - ScrollX);

    /// <summary>Screen x → world x, for the layers that are generated from the
    /// position they sit at rather than stored.</summary>
    private double WorldX(double screenX) => (screenX + ScrollX) / (Zoom <= 0 ? 1 : Zoom);

    /// <summary>Where a star is actually drawn: its moment, plus the sideways nudge
    /// that breaks a knot of entries into a cluster and fades away as the zoom starts
    /// separating them for real.</summary>
    private float StarX(in SkyStar star) =>
        ScreenX(star.X) + (float)(star.NudgeX * ConstellationLayout.NudgeFade(Zoom));

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        DrawAtmosphere(canvas, rect);
        DrawBubbles(canvas, rect);
        DrawDust(canvas, rect);
        DrawAsterism(canvas, rect);
        DrawPath(canvas, rect);
        DrawTicks(canvas, rect);
        DrawStars(canvas, rect);
        DrawVignette(canvas, rect);
    }

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
    private void DrawAsterism(ICanvas canvas, RectF rect)
    {
        if (!Asterism.HasShape)
            return;

        var stars = Asterism.Stars;

        float X(int i) => rect.X + (float)stars[i].X * rect.Width;
        float Y(int i) => rect.Y + (float)stars[i].Y * rect.Height;

        // Dim. The figure is the room, not the record — if it can compete with an event
        // for attention, it is drawn wrong.
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = _asterism.WithAlpha(0.10f);
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

            canvas.FillColor = _asterism.WithAlpha(0.07f * brightness);
            canvas.FillCircle(X(i), Y(i), radius * 2.8f);
            canvas.FillColor = _asterism.WithAlpha(0.22f + 0.2f * brightness);
            canvas.FillCircle(X(i), Y(i), radius);
        }
    }

    // ── The line ─────────────────────────────────────────────────────────────────

    /// <summary>A thin glowing path rather than an axis: two strokes, the wide one
    /// nearly transparent. Sampled every few pixels because it is a sine sum, not a
    /// data series — there are no points to join.</summary>
    private void DrawPath(ICanvas canvas, RectF rect)
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

        canvas.StrokeColor = _pathColor.WithAlpha(0.11f);
        canvas.StrokeSize = 7f;
        canvas.DrawPath(path);

        canvas.StrokeColor = _pathColor.WithAlpha(0.5f);
        canvas.StrokeSize = 1.2f;
        canvas.DrawPath(path);
    }

    /// <summary>Dates, floated at the foot of the sky with no rule under them. The one
    /// axis that means anything is time, so it is the one that gets labels — and even
    /// it gets no line.</summary>
    private void DrawTicks(ICanvas canvas, RectF rect)
    {
        if (Ticks.Count == 0)
            return;

        canvas.Font = Font.Default;
        canvas.FontSize = 10f;
        canvas.FontColor = _tickColor.WithAlpha(0.55f);

        foreach (var tick in Ticks)
        {
            var x = ScreenX(tick.X) + rect.X;
            if (x < rect.X - 40 || x > rect.Right + 40)
                continue;

            canvas.DrawString(tick.Label, x, rect.Bottom - 7f, HorizontalAlignment.Center);
        }
    }

    // ── The events ───────────────────────────────────────────────────────────────

    private void DrawStars(ICanvas canvas, RectF rect)
    {
        if (Stars.Count == 0)
            return;

        // Size comes from how much room each star has ON SCREEN — the world's width
        // times the magnification. So zooming in genuinely grows the symbols back to
        // full size as the crowd around them thins out: the reveal is one rule, not a
        // second code path (see ConstellationLayout.StarRadius).
        var screenWidth = (WorldWidth > 0 ? WorldWidth : rect.Width) * (Zoom <= 0 ? 1 : Zoom);
        var radius = (float)ConstellationLayout.StarRadius(screenWidth, Events.Count);

        // Glow needs ROOM, not just size. A halo is wider than its symbol, so on a run
        // of daily entries — one mood a day, ten pixels apart — every halo touched its
        // neighbours' and fifteen stars welded into one fuzzy caterpillar with no
        // symbol left in it. Where the sky is that busy, the stars go bare.
        var spacing = Events.Count > 0 ? screenWidth / Events.Count : double.MaxValue;
        var glow = radius >= GlowRadiusFloor && spacing > radius * 3.0;

        var margin = radius * 4f + 8f;

        DrawLinks(canvas, rect, margin);

        for (int i = 0; i < Stars.Count; i++)
        {
            var star = Stars[i];
            var x = StarX(star);
            if (x < -margin || x > rect.Width + margin)
                continue;

            var selected = i == SelectedIndex;
            // Stars and events are placed one-for-one and in the same order; the guard
            // is for the frame between a new load and its re-place, not a real state.
            var category = i < Events.Count ? Events[i].Category : CelestialCategory.Custom;
            var color = ColorFor(category);

            CelestialSymbols.Draw(
                canvas,
                category,
                rect.X + x,
                rect.Y + (float)star.Y,
                selected ? MathF.Max(radius * 1.6f, 6f) : radius,
                color,
                glow || selected,
                // The alternating hand-made tilt every icon tile in this app wears
                // (TimelineItem.IconRotation), in radians.
                tilt: i % 2 == 0 ? -0.11f : 0.09f);

            if (selected)
            {
                canvas.StrokeColor = color.WithAlpha(0.75f);
                canvas.StrokeSize = 1.2f;
                canvas.DrawCircle(rect.X + x, rect.Y + (float)star.Y, MathF.Max(radius * 3.4f, 15f));
            }
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
                rect.X + ox, rect.Y + (float)other.Y,
                rect.X + x, rect.Y + (float)star.Y);
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
