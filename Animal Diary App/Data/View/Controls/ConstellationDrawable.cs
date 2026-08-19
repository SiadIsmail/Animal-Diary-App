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
//  ── WHAT IS DRAWN, AND WHAT IS NOT ──
//
//  Drawn: the hours, the dates, and one symbol per recorded entry. That is the
//  whole list.
//
//  NOT drawn, and each was removed for the same reason — it looked like data and
//  was not: a wandering path, hairlines joining entries, a figure of joined stars
//  behind everything, drifting bubbles, a field of dust. Every one of them put
//  marks on the canvas that a first-time reader had to work out the meaning of,
//  and none of them had one. On a surface whose job is legibility, decoration that
//  resembles the data is worse than no decoration.
//
//  What is left of the atmosphere is the part that CANNOT be mistaken for a
//  reading: a dark ground, and a soft daylight band which is itself the hour
//  reference. It is prettier for having less in it.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A label worth drawing, and where.
/// </summary>
/// <param name="X">On the History lens this is a WORLD x and the camera applies to
/// it; on the Cycle ring it is a canvas position, because a ring does not pan.</param>
/// <param name="Y">Canvas y.</param>
/// <param name="Pinned">Stays put while the content scrolls under it — the hours in
/// the left gutter belong to the card, the dates belong to the timeline.</param>
public readonly record struct SkyTick(double X, double Y, string Label, bool Pinned = false);

public sealed class ConstellationDrawable : IDrawable
{
    // ── What to draw (assigned by the page; never mutated here) ──────────────────
    public IReadOnlyList<CelestialEvent> Events { get; set; } = Array.Empty<CelestialEvent>();
    public IReadOnlyList<SkyStar> Stars { get; set; } = Array.Empty<SkyStar>();
    public IReadOnlyList<SkyTick> Ticks { get; set; } = Array.Empty<SkyTick>();

    /// <summary>Which way the sky is arranged.</summary>
    public SkyLens Lens { get; set; } = SkyLens.History;

    /// <summary>The one decorative choice left: the colour the ground leans towards.</summary>
    public SkySignature Signature
    {
        get => _signature;
        set { _signature = value; MixPalette(); }
    }

    private SkySignature _signature = SkySignature.Default;

    // ── The camera (History only — a ring is not panned) ─────────────────────────
    public double ScrollX { get; set; }
    public double Zoom { get; set; } = 1;

    /// <summary>The stretch's width in WORLD units: the plot area at zoom 1, which is
    /// the card minus the hour gutter.</summary>
    public double WorldWidth { get; set; }

    /// <summary>The tapped star, or -1. The only thing on this canvas that is
    /// emphasised, and it is emphasised because a finger chose it — never because of
    /// what it holds.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>The kinds brought forward; empty means all of them. Several at once,
    /// because the question is usually about two things — the nights he seized against
    /// the nights the evening dose went in. Nothing is hidden: the rest is dimmed.</summary>
    public IReadOnlyCollection<CelestialCategory> Focus { get; set; } = Array.Empty<CelestialCategory>();

    // ── The lens change, in flight ───────────────────────────────────────────────
    // Flown rather than cut, because the transformation teaches itself: watching the
    // entries lift off a date/time grid and wrap onto a ring is how someone works out
    // what the ring is without reading a sentence. The tween runs in SCREEN space —
    // each lens has its own camera, and interpolating two cameras as well as two
    // layouts is a knot with nothing to show for it.
    public double Transition { get; set; } = 1;
    public SkyLens FromLens { get; set; } = SkyLens.History;
    public IReadOnlyList<PointF> TweenFrom { get; set; } = Array.Empty<PointF>();
    public IReadOnlyList<PointF> TweenTo { get; set; } = Array.Empty<PointF>();

    private bool InFlight => Transition < 1 && TweenFrom.Count > 0 && TweenFrom.Count == TweenTo.Count;

    // ── Palette (resolved through AppColors, never hex literals) ─────────────────
    private readonly Color _skyTop = AppColors.Resolve("NightTop", Color.FromArgb("#123C3E"));
    private readonly Color _skyMid = AppColors.Resolve("NightMid", Color.FromArgb("#0B2A31"));
    private readonly Color _skyBottom = AppColors.Resolve("NightBottom", Color.FromArgb("#08191F"));
    private readonly Color _daylight = AppColors.Resolve("NightGlowSand", Color.FromArgb("#EDDFC0"));
    private readonly Color _ruleBase = AppColors.Resolve("SkyPath", Color.FromArgb("#7FD4C4"));
    private readonly Color _tickColor = AppColors.Resolve("SkyInk", Color.FromArgb("#9FC4C0"));

    private readonly Color[] _starColors = CelestialVisuals.All
        .Select(c => AppColors.Resolve(CelestialVisuals.For(c).ColorKey, Colors.White))
        .ToArray();

    private Color _rule;

    public ConstellationDrawable() => MixPalette();

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_rule))]
    private void MixPalette()
    {
        var accent = AppColors.Resolve(_signature.AccentKey, Color.FromArgb("#6FBFB2"));
        _rule = Blend(_ruleBase, accent, 0.3f);
    }

    private static Color Blend(Color from, Color to, float amount) => new(
        from.Red + (to.Red - from.Red) * amount,
        from.Green + (to.Green - from.Green) * amount,
        from.Blue + (to.Blue - from.Blue) * amount);

    /// <summary>Below this radius a star's halo is dropped: a halo is wider than its
    /// symbol, so on a run of daily entries every halo touches its neighbours' and a
    /// dozen stars weld into one smudge with no symbol left in it.</summary>
    private const float GlowRadiusFloor = 3.6f;

    /// <summary>World x → screen x, past the hour gutter. Only History has a camera.</summary>
    private float ScreenX(double worldX) => Lens == SkyLens.History
        ? (float)(worldX * Zoom - ScrollX + ConstellationLayout.HourGutter)
        : (float)worldX;

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var t = (float)Math.Clamp(Transition, 0, 1);

        DrawGround(canvas, rect);

        if (t < 1 && FromLens != Lens)
        {
            DrawFrame(canvas, rect, FromLens, 1 - t);
            DrawFrame(canvas, rect, Lens, t);
        }
        else
        {
            DrawFrame(canvas, rect, Lens, 1);
        }

        DrawTicks(canvas, rect, t);
        DrawStars(canvas, rect, t);
    }

    /// <summary>The dark ground, and nothing on it. Three stops of the rockpool wash
    /// after dark — the same gradient every other page paints in daylight.</summary>
    private void DrawGround(ICanvas canvas, RectF rect)
    {
        // Fenced: a paint set with SetFillPaint is canvas state and on Android survives
        // a later FillColor assignment, which silently blanks every fill after it.
        // See AI/known-constraints.md.
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
    }

    private void DrawFrame(ICanvas canvas, RectF rect, SkyLens lens, float alpha)
    {
        if (lens == SkyLens.Cycle)
            DrawRing(canvas, rect, alpha);
        else
            DrawHours(canvas, rect, alpha);
    }

    /// <summary>
    /// The History lens's frame: a daylight band and four hour rules.
    ///
    /// <para><b>The band is the axis and the atmosphere at once.</b> With the middle of
    /// the day lit and the small hours left dark, "these keep happening in the middle
    /// of the night" is visible before a single label has been read — and it is drawn
    /// from clock time alone, so it asserts nothing. A gradient rather than two hard
    /// edges, because dawn is not a boundary.</para>
    ///
    /// <para>The rules are the one place this app draws gridlines, and the exception is
    /// principled: the ban exists so a VALUE axis can never imply a good or bad
    /// direction. Both axes here are time. An unlabelled time axis is not restraint,
    /// it is a puzzle.</para>
    /// </summary>
    private void DrawHours(ICanvas canvas, RectF rect, float alpha)
    {
        var gutter = (float)ConstellationLayout.HourGutter;
        var left = rect.X + gutter;
        var width = rect.Width - gutter;
        if (width <= 0)
            return;

        var top = rect.Y + (float)ConstellationLayout.HourY(0, rect.Height);
        var bottom = rect.Y + (float)ConstellationLayout.HourY(1, rect.Height);
        var band = new RectF(left, top, width, bottom - top);

        canvas.SaveState();
        canvas.SetFillPaint(new LinearGradientPaint
        {
            GradientStops = new[]
            {
                new PaintGradientStop(0f, _daylight.WithAlpha(0f)),
                new PaintGradientStop(0.27f, _daylight.WithAlpha(0.05f * alpha)),
                new PaintGradientStop(0.5f, _daylight.WithAlpha(0.085f * alpha)),
                new PaintGradientStop(0.73f, _daylight.WithAlpha(0.05f * alpha)),
                new PaintGradientStop(1f, _daylight.WithAlpha(0f)),
            },
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        }, band);
        canvas.FillRectangle(band);
        canvas.RestoreState();

        canvas.StrokeSize = 1f;
        canvas.StrokeColor = _rule.WithAlpha(0.16f * alpha);
        for (int hour = 0; hour <= 24; hour += 6)
        {
            var y = rect.Y + (float)ConstellationLayout.HourY(hour / 24.0, rect.Height);
            canvas.DrawLine(left, y, rect.Right, y);
        }
    }

    /// <summary>
    /// The Cycle lens's frame: the rim, the hole in the middle, and four quarter marks.
    ///
    /// <para>The inner circle is drawn on purpose. The distance out from the centre is
    /// how far through the stretch an entry was, and a radius with no visible start is
    /// the question "what does the middle mean?" — the ring shows where the scale
    /// begins, and the caption under the sky says which way it runs.</para>
    /// </summary>
    private void DrawRing(ICanvas canvas, RectF rect, float alpha)
    {
        var centreX = rect.X + rect.Width / 2;
        var centreY = rect.Y + rect.Height / 2;
        var outer = (float)ConstellationLayout.RingOuter(rect.Width, rect.Height);
        if (outer <= 0)
            return;

        var inner = outer * (float)ConstellationLayout.RingInnerFraction;

        canvas.StrokeLineCap = LineCap.Round;

        canvas.StrokeColor = _rule.WithAlpha(0.34f * alpha);
        canvas.StrokeSize = 1f;
        canvas.DrawCircle(centreX, centreY, outer);

        canvas.StrokeColor = _rule.WithAlpha(0.2f * alpha);
        canvas.DrawCircle(centreX, centreY, inner);

        canvas.StrokeColor = _rule.WithAlpha(0.22f * alpha);
        for (int q = 0; q < 4; q++)
        {
            var angle = q / 4f * MathF.Tau - MathF.PI / 2f;
            var cos = MathF.Cos(angle);
            var sin = MathF.Sin(angle);
            canvas.DrawLine(
                centreX + cos * inner, centreY + sin * inner,
                centreX + cos * outer, centreY + sin * outer);
        }
    }

    /// <summary>The hours down the gutter and the dates along the foot. The only
    /// lettering on the canvas, and the reason the axes are readable at all.</summary>
    private void DrawTicks(ICanvas canvas, RectF rect, float alpha)
    {
        if (Ticks.Count == 0 || alpha <= 0.01f)
            return;

        canvas.Font = Font.Default;
        canvas.FontSize = 10f;
        canvas.FontColor = _tickColor.WithAlpha(0.7f * alpha);

        foreach (var tick in Ticks)
        {
            var x = rect.X + (tick.Pinned || Lens != SkyLens.History
                ? (float)tick.X
                : ScreenX(tick.X));

            if (x < rect.X - 40 || x > rect.Right + 40)
                continue;

            canvas.DrawString(tick.Label, x, rect.Y + (float)tick.Y, HorizontalAlignment.Center);
        }
    }

    // ── The events ───────────────────────────────────────────────────────────────

    private void DrawStars(ICanvas canvas, RectF rect, float t)
    {
        var screenWidth = (WorldWidth > 0 ? WorldWidth : rect.Width) * (Zoom <= 0 ? 1 : Zoom);
        var radius = (float)ConstellationLayout.StarRadius(screenWidth, Events.Count);

        if (InFlight)
        {
            DrawStarsInFlight(canvas, rect, t, radius);
            return;
        }

        if (Stars.Count == 0)
            return;

        // Glow needs ROOM, not just size — see GlowRadiusFloor.
        var spacing = Events.Count > 0 ? screenWidth / Events.Count : double.MaxValue;
        var glow = radius >= GlowRadiusFloor && spacing > radius * 3.0;
        var margin = radius * 4f + 8f;

        // Two passes when anything is in focus, so the quiet ones are laid down first
        // and the focused kinds sit on top rather than being buried by whatever
        // happened to be logged later.
        var passes = Focus.Count == 0 ? 1 : 2;
        for (int pass = 0; pass < passes; pass++)
        for (int i = 0; i < Stars.Count; i++)
        {
            var x = ScreenX(Stars[i].X);
            if (x < -margin || x > rect.Width + margin)
                continue;

            var dimmed = OutOfFocus(i);
            if (Focus.Count > 0 && dimmed == (pass == 1))
                continue;

            var selected = i == SelectedIndex;
            var category = i < Events.Count ? Events[i].Category : CelestialCategory.Custom;
            var color = dimmed ? ColorFor(category).WithAlpha(0.26f) : ColorFor(category);

            CelestialSymbols.Draw(
                canvas, category,
                rect.X + x, rect.Y + (float)Stars[i].Y,
                selected ? radius * 1.5f : radius,
                color,
                (glow || selected) && !dimmed);

            if (selected)
            {
                canvas.StrokeColor = color.WithAlpha(0.8f);
                canvas.StrokeSize = 1.2f;
                canvas.DrawCircle(rect.X + x, rect.Y + (float)Stars[i].Y, MathF.Max(radius * 3.2f, 15f));
            }
        }
    }

    /// <summary>Mid-flight: every star between where it was and where it is going.
    /// Nothing else is drawn — the only thing worth watching during a lens change is
    /// the entries themselves moving.</summary>
    private void DrawStarsInFlight(ICanvas canvas, RectF rect, float t, float radius)
    {
        for (int i = 0; i < TweenFrom.Count && i < Events.Count; i++)
        {
            var from = TweenFrom[i];
            var to = TweenTo[i];

            var x = rect.X + from.X + (to.X - from.X) * t;
            var y = rect.Y + from.Y + (to.Y - from.Y) * t;
            if (x < rect.X - 40 || x > rect.Right + 40)
                continue;

            var category = Events[i].Category;
            var color = OutOfFocus(i) ? ColorFor(category).WithAlpha(0.26f) : ColorFor(category);

            CelestialSymbols.Draw(canvas, category, x, y, radius, color, glow: false);
        }
    }

    private bool OutOfFocus(int index) =>
        Focus.Count > 0 && index < Events.Count && !Focus.Contains(Events[index].Category);

    private Color ColorFor(CelestialCategory category)
    {
        var i = (int)category;
        return i >= 0 && i < _starColors.Length ? _starColors[i] : _starColors[^1];
    }
}
