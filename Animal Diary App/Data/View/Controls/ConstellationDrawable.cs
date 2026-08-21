namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
//  The sky.
//
//  Pure Microsoft.Maui.Graphics, like every other chart in this app, no charting
//  package, because nothing on the drawing path may drag in a native library
//  (AI/known-constraints.md, and the two times this repo has already paid for it).
//
//  ── BORING AT REST, ALIVE WHEN TOUCHED ──
//
//  The resting picture is deliberately plain: the hours, the dates, and one symbol
//  per entry. An earlier version filled a meaningless vertical axis with ornament,
//  a wandering path, hairlines between entries, a figure of joined stars, drifting
//  bubbles, a field of dust, and it was pretty and unreadable. The lesson was not
//  "no beauty"; it was that ornament competing with data AT REST costs comprehension
//  and buys nothing.
//
//  So everything expressive here happens in RESPONSE TO THE READER, where the only
//  cost is delight and the payload is usually information:
//
//    • the sky writes itself left to right on load, which teaches the date axis
//    • tapping a star lights its whole day, which IS the "Also that day" list
//    • focusing a kind blooms it and chains it, which makes the intervals visible
//
//  ── Marks vs fields ──
//  What may sit on the canvas at rest is decided by one test: could a reader mistake
//  it for an entry? MARK-like things (dots, joined dots, small shapes) never may.
//  FIELD-like things (a gradient, a broad glow, a vignette) always may, because
//  nothing about them resembles a reading. That is the whole of the atmosphere here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A label worth drawing, and where.
/// </summary>
/// <param name="X">On the History lens this is a WORLD x and the camera applies to
/// it; on the Cycle ring it is a canvas position, because a ring does not pan.</param>
/// <param name="Y">Canvas y.</param>
/// <param name="Pinned">Stays put while the content scrolls under it: the hours in
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

    // ── The camera (History only: a ring is not panned) ─────────────────────────
    public double ScrollX { get; set; }
    public double Zoom { get; set; } = 1;

    /// <summary>The stretch's width in WORLD units: the plot area at zoom 1, which is
    /// the card minus the hour gutter.</summary>
    public double WorldWidth { get; set; }

    /// <summary>The tapped star, or -1. The only thing on this canvas that is
    /// emphasised, and it is emphasised because a finger chose it, never because of
    /// what it holds.</summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>The kinds brought forward; empty means all of them.</summary>
    public IReadOnlyCollection<CelestialCategory> Focus { get; set; } = Array.Empty<CelestialCategory>();

    // ── The three moments (all 1 at rest; the page animates them) ────────────────

    /// <summary>The load. Stars arrive staggered along the date axis, so the history
    /// visibly writes itself left to right, which is the axis explaining itself
    /// before anyone has read the caption.</summary>
    public double Reveal { get; set; } = 1;

    /// <summary>The tap. A ripple off the chosen star, and its whole day lit.</summary>
    public double Selection { get; set; } = 1;

    /// <summary>The focus change. The unfocused recede while the focused kind blooms
    /// and its entries chain together.</summary>
    public double Bloom { get; set; } = 1;

    /// <summary>The day the selected entry belongs to, for lighting its siblings.</summary>
    public DateTime? HighlightDay { get; set; }

    /// <summary>That day's span in WORLD units (History only: a ring has no columns).</summary>
    public double HighlightFromX { get; set; }
    public double HighlightToX { get; set; }

    // ── The lens change, in flight ───────────────────────────────────────────────
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
    private Color _accent;

    public ConstellationDrawable() => MixPalette();

    [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(_rule), nameof(_accent))]
    private void MixPalette()
    {
        _accent = AppColors.Resolve(_signature.AccentKey, Color.FromArgb("#6FBFB2"));
        _rule = Blend(_ruleBase, _accent, 0.3f);
    }

    private static Color Blend(Color from, Color to, float amount) => new(
        from.Red + (to.Red - from.Red) * amount,
        from.Green + (to.Green - from.Green) * amount,
        from.Blue + (to.Blue - from.Blue) * amount);

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;

    /// <summary>Below this radius a star's halo is dropped: a halo is wider than its
    /// symbol, so on a run of daily entries every halo touches its neighbours' and a
    /// dozen stars weld into one smudge with no symbol left in it.</summary>
    private const float GlowRadiusFloor = 3.6f;

    /// <summary>A chain longer than this is a scribble, not a rhythm. Twice-daily
    /// medication over a year is seven hundred links; nobody reads that.</summary>
    private const int MaxChainLinks = 400;

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

        DrawWavefront(canvas, rect);
        DrawDayColumn(canvas, rect);
        DrawTicks(canvas, rect, t);
        DrawChains(canvas, rect);
        DrawStars(canvas, rect, t);
        DrawVignette(canvas, rect);
    }

    /// <summary>
    /// The dark ground and one broad glow: both FIELD-like, so neither can be read as
    /// an entry. This is the whole of the resting atmosphere, and it is what stops the
    /// card being a flat rectangle without putting a single ambiguous mark on it.
    /// </summary>
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

        // The pet's own colour, once, very softly, anchored to the card rather than to
        // time: an atmosphere that scrolled would start to look like it meant something.
        var radius = rect.Width * 0.8f;
        var bounds = new RectF(
            rect.X + rect.Width * 0.18f - radius,
            rect.Y + rect.Height * 0.1f - radius,
            radius * 2, radius * 2);

        canvas.SaveState();
        canvas.SetFillPaint(new RadialGradientPaint
        {
            StartColor = _accent.WithAlpha(0.17f),
            EndColor = _accent.WithAlpha(0f),
            Center = new Point(0.5, 0.5),
            Radius = 0.5
        }, bounds);
        canvas.FillRectangle(bounds);
        canvas.RestoreState();
    }

    /// <summary>The corners darkened, so the card reads as a window onto a night rather
    /// than a rectangle painted dark, and so a bright star at the edge is not cut in
    /// half by the rounded corner.</summary>
    private void DrawVignette(ICanvas canvas, RectF rect)
    {
        var radius = MathF.Max(rect.Width, rect.Height) * 0.78f;
        var bounds = new RectF(
            rect.Center.X - radius, rect.Center.Y - radius, radius * 2, radius * 2);

        canvas.SaveState();
        canvas.SetFillPaint(new RadialGradientPaint
        {
            StartColor = _skyBottom.WithAlpha(0f),
            EndColor = _skyBottom.WithAlpha(0.5f),
            Center = new Point(0.5, 0.5),
            Radius = 0.5
        }, bounds);
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
    /// The History lens's frame: a daylight band and hour rules.
    ///
    /// <para><b>The band is the axis and the atmosphere at once.</b> With the middle of
    /// the day lit and the small hours left dark, "these keep happening in the middle
    /// of the night" is visible before a single label has been read, and it is drawn
    /// from clock time alone, so it asserts nothing. A gradient rather than two hard
    /// edges, because dawn is not a boundary.</para>
    ///
    /// <para>The rules are the one place this app draws gridlines, and the exception is
    /// principled: the ban exists so a VALUE axis can never imply a good or bad
    /// direction. Both axes here are time. An unlabelled time axis is not restraint,
    /// it is a puzzle. They densify as you zoom in: six hours, then three, then one,
    /// which is the picture telling you more the closer you look.</para>
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
        var step = HourStep();
        for (int hour = 0; hour <= 24; hour += step)
        {
            // The quarters stay the anchors however fine the rules get.
            var major = hour % 6 == 0;
            canvas.StrokeColor = _rule.WithAlpha((major ? 0.16f : 0.07f) * alpha);

            var y = rect.Y + (float)ConstellationLayout.HourY(hour / 24.0, rect.Height);
            canvas.DrawLine(left, y, rect.Right, y);
        }
    }

    /// <summary>Six hours at rest, three then one as the days spread out. Chosen off the
    /// width one day occupies, so it follows the zoom rather than a magic number.</summary>
    private int HourStep()
    {
        var dayWidth = WorldWidth <= 0 ? 0 : WorldWidth * Zoom / Math.Max(1, VisibleDays());
        if (dayWidth >= 900) return 1;
        if (dayWidth >= 260) return 3;
        return 6;
    }

    /// <summary>How many days the world spans. Derived from the entries rather than
    /// stored, because the drawable is not told the range.</summary>
    private double VisibleDays()
    {
        if (Events.Count < 2)
            return 1;

        return Math.Max(1, (Events[^1].When - Events[0].When).TotalDays);
    }

    /// <summary>
    /// The Cycle lens's frame: the rim, the hole in the middle, and four quarter marks.
    ///
    /// <para>The inner circle is drawn on purpose. The distance out from the centre is
    /// how far through the stretch an entry was, and a radius with no visible start is
    /// the question "what does the middle mean?": the ring shows where the scale
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

    /// <summary>
    /// The leading edge of the load, sweeping left to right.
    ///
    /// <para>A per-star fade is invisible on a busy timeline: three-pixel symbols
    /// fading up among thousands of others is not an animation anyone sees. This is a
    /// FIELD (a soft vertical gradient, never a mark), so it reads at any density and
    /// cannot be mistaken for an entry, and it makes the point the stagger is there to
    /// make: the history is being written from the oldest end to the newest.</para>
    /// </summary>
    private void DrawWavefront(ICanvas canvas, RectF rect)
    {
        var reveal = (float)Math.Clamp(Reveal, 0, 1);
        if (reveal >= 1 || Lens != SkyLens.History || Stars.Count == 0 || WorldWidth <= 0)
            return;

        // Where the frontier has reached: the star at fraction f begins arriving at
        // reveal = StaggerShare × f.
        var frontier = MathF.Min(1f, reveal / (float)ConstellationLayout.StaggerShare);
        var x = ScreenX(frontier * WorldWidth) + rect.X;
        var width = MathF.Max(rect.Width * 0.16f, 40f);

        // Fades out as the sweep finishes, so it never lingers over the resting picture.
        var strength = 0.5f * (1 - reveal) * (1 - reveal);
        var bounds = new RectF(x - width, rect.Y, width * 2, rect.Height);

        canvas.SaveState();
        canvas.SetFillPaint(new LinearGradientPaint
        {
            GradientStops = new[]
            {
                new PaintGradientStop(0f, _rule.WithAlpha(0f)),
                new PaintGradientStop(0.5f, _rule.WithAlpha(strength)),
                new PaintGradientStop(1f, _rule.WithAlpha(0f)),
            },
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        }, bounds);
        canvas.FillRectangle(bounds);
        canvas.RestoreState();
    }

    /// <summary>
    /// The tapped entry's whole day, lit.
    ///
    /// <para>This is "Also that day" happening on the canvas instead of only in the
    /// sheet: the column says which slice of time the sheet is talking about, and the
    /// siblings brightening (in <see cref="DrawStars"/>) says which entries those are.
    /// The flourish and the information are the same thing.</para>
    /// </summary>
    private void DrawDayColumn(ICanvas canvas, RectF rect)
    {
        if (Lens != SkyLens.History || HighlightDay is null || HighlightToX <= HighlightFromX)
            return;

        var left = ScreenX(HighlightFromX) + rect.X;
        var right = ScreenX(HighlightToX) + rect.X;

        // A day can be a fraction of a pixel wide across a year; give the column a
        // minimum so it is a mark rather than a rounding error.
        var width = MathF.Max(right - left, 10f);
        var centre = (left + right) / 2;
        var bounds = new RectF(centre - width / 2, rect.Y, width, rect.Height);

        if (bounds.Right < rect.X || bounds.Left > rect.Right)
            return;

        canvas.FillColor = _rule.WithAlpha(0.1f * (float)Selection);
        canvas.FillRectangle(bounds);
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

    /// <summary>
    /// The focused kind, joined in time order.
    ///
    /// <para>Connections were removed from the resting picture because unexplained
    /// hairlines between unrelated entries were ornament that looked like data. These
    /// are the opposite: they exist only for a kind the reader deliberately picked, and
    /// they mean exactly one thing: <b>the next one of these</b>. The length of a link
    /// is the interval, so "the gaps are getting shorter" becomes something seen rather
    /// than computed, and because y is the time of day the chain zig-zags and can never
    /// be misread as a fitted trend line.</para>
    /// </summary>
    private void DrawChains(ICanvas canvas, RectF rect)
    {
        if (Focus.Count == 0 || Bloom <= 0.01 || InFlight || Stars.Count == 0)
            return;

        canvas.StrokeSize = 1f;
        canvas.StrokeLineCap = LineCap.Round;

        foreach (var category in Focus)
        {
            var indices = new List<int>();
            for (int i = 0; i < Stars.Count && i < Events.Count; i++)
                if (Events[i].Category == category)
                    indices.Add(i);

            if (indices.Count < 2 || indices.Count > MaxChainLinks)
                continue;

            canvas.StrokeColor = ColorFor(category).WithAlpha(0.3f * (float)Bloom);

            for (int k = 1; k < indices.Count; k++)
            {
                var a = Stars[indices[k - 1]];
                var b = Stars[indices[k]];
                canvas.DrawLine(
                    rect.X + ScreenX(a.X), rect.Y + (float)a.Y,
                    rect.X + ScreenX(b.X), rect.Y + (float)b.Y);
            }
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

        // Glow needs ROOM, not just size: see GlowRadiusFloor.
        var spacing = Events.Count > 0 ? screenWidth / Events.Count : double.MaxValue;
        var ambientGlow = radius >= GlowRadiusFloor && spacing > radius * 3.0;
        var margin = radius * 4f + 8f;
        var bloom = (float)Bloom;

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

            var arrival = ArrivalOf(i);
            if (arrival <= 0.01f)
                continue;

            var arriving = arrival < 0.98f;

            var selected = i == SelectedIndex;
            var sibling = !selected && i < Events.Count
                && HighlightDay is DateTime day && Events[i].When.Date == day;

            var category = i < Events.Count ? Events[i].Category : CelestialCategory.Custom;
            var color = ColorFor(category);

            // Out of focus: recedes rather than merely fading: smaller and quieter, so
            // the focused kind reads as brought forward instead of the rest being
            // switched off. Nothing is hidden; the context is the point.
            var scale = dimmed ? Lerp(1f, 0.74f, bloom) : 1f;
            var alpha = dimmed ? Lerp(1f, 0.26f, bloom) : 1f;

            if (selected)
                scale *= 1.5f;
            else if (sibling)
                scale *= Lerp(1.45f, 1f, (float)Selection);

            // Arriving stars fade up and grow into place.
            alpha *= arrival;
            scale *= Lerp(0.55f, 1f, arrival);

            CelestialSymbols.Draw(
                canvas, category,
                rect.X + x, rect.Y + (float)Stars[i].Y,
                radius * scale,
                color.WithAlpha(alpha),
                // Focus buys the halo back for the kind that was chosen, however busy
                // the sky is, that is the bloom. So does ARRIVING: a star sparks as it
                // lands and settles afterwards, which is what makes the reveal visible
                // in a sky too dense for an ambient glow. Suppressing the halo while a
                // star arrived (the first version did) left the animation as a fade
                // of three-pixel dots, which on a busy timeline is no animation at all.
                arriving || ambientGlow || selected || sibling || (!dimmed && Focus.Count > 0));

            if (selected)
                DrawRipple(canvas, rect.X + x, rect.Y + (float)Stars[i].Y, radius, color);
        }
    }

    /// <summary>The ring that answers a tap: it expands away from the star and fades as
    /// it goes, then a steady ring remains so the chosen entry stays findable while its
    /// sheet is open.</summary>
    private void DrawRipple(ICanvas canvas, float x, float y, float radius, Color color)
    {
        var s = (float)Math.Clamp(Selection, 0, 1);

        if (s < 1)
        {
            canvas.StrokeColor = color.WithAlpha(0.5f * (1 - s));
            canvas.StrokeSize = 1.4f;
            canvas.DrawCircle(x, y, MathF.Max(radius * 3.2f, 15f) * Lerp(0.4f, 2.1f, s));
        }

        canvas.StrokeColor = color.WithAlpha(0.8f * s);
        canvas.StrokeSize = 1.2f;
        canvas.DrawCircle(x, y, MathF.Max(radius * 3.2f, 15f));
    }

    /// <summary>How far into its own arrival a star is. The stagger itself lives in
    /// <c>ConstellationLayout</c>: it is the one ornament that teaches something, so it
    /// is pinned by a test rather than left in the painter. On the ring, where
    /// left-to-right means nothing, they simply arrive together.</summary>
    private float ArrivalOf(int index)
    {
        var fraction = Lens == SkyLens.History && WorldWidth > 0
            ? Stars[index].X / WorldWidth
            : 0;

        return (float)ConstellationLayout.ArrivalOf(Reveal, fraction);
    }

    /// <summary>Mid-flight: every star between where it was and where it is going.
    /// Nothing else is drawn: the only thing worth watching during a lens change is
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
