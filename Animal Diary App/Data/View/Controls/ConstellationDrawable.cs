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

    /// <summary>How far the sky has been dragged, in canvas units. Content and canvas
    /// share one unit, so a star's screen x is simply <c>Star.X - ScrollX</c>.</summary>
    public double ScrollX { get; set; }

    /// <summary>The whole stretch's width in canvas units — viewport × zoom. The star
    /// size is read from it, so it has to be set alongside <see cref="Stars"/>.</summary>
    public double ContentWidth { get; set; }

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
    private readonly Color _bubbleTint = AppColors.Resolve("Teal", Color.FromArgb("#149081"));
    private readonly Color _dust = AppColors.Resolve("StarDust", Color.FromArgb("#C4E6E3"));
    private readonly Color _pathColor = AppColors.Resolve("SkyPath", Color.FromArgb("#7FD4C4"));
    private readonly Color _tickColor = AppColors.Resolve("SkyInk", Color.FromArgb("#9FC4C0"));

    private readonly Color[] _starColors = CelestialVisuals.All
        .Select(c => AppColors.Resolve(CelestialVisuals.For(c).ColorKey, Colors.White))
        .ToArray();

    /// <summary>Below this radius a star's halo is dropped. Not a count: a halo is
    /// three fills and roughly three times the symbol's width, so in a crowded sky it
    /// is both the slow part AND the part that welds neighbours into one blob.</summary>
    private const float GlowRadiusFloor = 3.8f;

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        DrawAtmosphere(canvas, rect);
        DrawBubbles(canvas, rect);
        DrawDust(canvas, rect);
        DrawPath(canvas, rect);
        DrawTicks(canvas, rect);
        DrawStars(canvas, rect);
    }

    // ── Atmosphere: pure decoration, and deliberately so ─────────────────────────

    private void DrawAtmosphere(ICanvas canvas, RectF rect)
    {
        // The same three-stop vertical wash WaterBackground paints, after dark.
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

        // Mint glow off the top-left corner, and warm sand rising off the bottom edge —
        // the two glows that make the app's background warm rather than clinical. They
        // are anchored to the CANVAS, not to time: an atmosphere that scrolled would
        // start to look like it meant something.
        Glow(canvas, _glowMint, 0.20f,
            new PointF(rect.X + rect.Width * 0.16f, rect.Y + rect.Height * 0.08f),
            rect.Width * 0.62f);

        Glow(canvas, _glowSand, 0.13f,
            new PointF(rect.X + rect.Width * 0.62f, rect.Bottom + rect.Height * 0.12f),
            rect.Width * 0.75f);
    }

    private static void Glow(ICanvas canvas, Color color, float strength, PointF centre, float radius)
    {
        var bounds = new RectF(centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        canvas.SetFillPaint(new RadialGradientPaint
        {
            StartColor = color.WithAlpha(strength),
            EndColor = color.WithAlpha(0f),
            Center = new Point(0.5, 0.5),
            Radius = 0.5
        }, bounds);
        canvas.FillRectangle(bounds);
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
            var h = Hash((uint)c * 2654435761u);

            var radius = 16f + (h & 0x3F) / 63f * 74f;
            var x = rect.X + c * cell + ((h >> 6) & 0x7F) / 127f * cell - start;
            var y = rect.Y + ((h >> 13) & 0xFF) / 255f * rect.Height;

            if (x + radius < rect.X || x - radius > rect.Right)
                continue;

            switch ((h >> 21) % 3)
            {
                case 0: // glass — a soft fill inside its ring
                    canvas.SetFillPaint(new RadialGradientPaint
                    {
                        StartColor = _bubbleGlass.WithAlpha(0.07f),
                        EndColor = _bubbleGlass.WithAlpha(0f),
                        Center = new Point(0.5, 0.5),
                        Radius = 0.5
                    }, new RectF(x - radius, y - radius, radius * 2, radius * 2));
                    canvas.FillCircle(x, y, radius);
                    canvas.StrokeColor = _bubbleGlass.WithAlpha(0.09f);
                    break;

                case 1: // outline
                    canvas.StrokeColor = _bubbleGlass.WithAlpha(0.07f);
                    break;

                default: // tint
                    canvas.StrokeColor = _bubbleTint.WithAlpha(0.3f);
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
                var h = Hash((uint)(c * 2 + n));
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
            var y = (float)ConstellationLayout.PathY(sx + ScrollX, rect.Height);
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
            var x = (float)(tick.X - ScrollX) + rect.X;
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

        // Size comes from how much room each star has, not from how many there are —
        // see ConstellationLayout.StarRadius. It is what makes zooming a reveal.
        var radius = (float)ConstellationLayout.StarRadius(
            ContentWidth > 0 ? ContentWidth : rect.Width, Events.Count);

        var glow = radius >= GlowRadiusFloor;
        var margin = radius * 4f + 8f;

        // Guides first, so every symbol sits on top of every thread.
        canvas.StrokeSize = 1f;
        canvas.StrokeColor = _pathColor.WithAlpha(0.18f);
        for (int i = 0; i < Stars.Count; i++)
        {
            var star = Stars[i];
            var x = (float)(star.X - ScrollX);
            if (x < -margin || x > rect.Width + margin)
                continue;

            if (Math.Abs(star.Offset) <= ConstellationLayout.GuideThreshold)
                continue;

            canvas.DrawLine(rect.X + x, rect.Y + (float)star.PathY, rect.X + x, rect.Y + (float)star.Y);
        }

        for (int i = 0; i < Stars.Count; i++)
        {
            var star = Stars[i];
            var x = (float)(star.X - ScrollX);
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
                glow || selected);

            if (selected)
            {
                canvas.StrokeColor = color.WithAlpha(0.75f);
                canvas.StrokeSize = 1.2f;
                canvas.DrawCircle(rect.X + x, rect.Y + (float)star.Y, MathF.Max(radius * 3.4f, 15f));
            }
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
