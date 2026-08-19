namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Data.Models;
using Microsoft.Maui.Graphics;

// ─────────────────────────────────────────────────────────────────────────────
//  The eight symbols.
//
//  Shape carries the meaning, and colour only reinforces it — a circle is a mood
//  whether or not the person looking can tell teal from blue, and whether or not
//  the phone is in direct sun. That is why these are drawn geometry rather than the
//  emoji the Journal chips wear: an emoji is a bitmap the platform picks, it renders
//  differently on every OS, and at five pixels across it is a smudge.
//
//  One drawing routine, two callers: the sky (thousands of them, tiny) and the
//  legend (eight of them, large). Neither may drift from the other — a legend that
//  disagreed with the sky would be worse than no legend.
// ─────────────────────────────────────────────────────────────────────────────

public static class CelestialSymbols
{
    /// <summary>
    /// Draw one symbol centred on (<paramref name="cx"/>, <paramref name="cy"/>).
    /// </summary>
    /// <param name="radius">Half the symbol's nominal size, in canvas units.</param>
    /// <param name="glow">Whether to lay a soft halo behind it. Off in a dense sky —
    /// three fills per star is what turns five thousand entries into a slideshow.</param>
    public static void Draw(
        ICanvas canvas, CelestialCategory category, float cx, float cy, float radius, Color color, bool glow)
    {
        if (radius <= 0)
            return;

        // Every symbol upright, always. The app tilts its icon tiles a degree or two on
        // its lists — imperfection on the frame — but a symbol here is the READOUT: the
        // same kind has to look identical everywhere it appears, or the legend stops
        // being a promise and the reader has to re-learn the shape on every screen.
        const float up = -MathF.PI / 2f;

        if (glow)
        {
            canvas.FillColor = color.WithAlpha(0.12f);
            canvas.FillCircle(cx, cy, radius * 1.9f);
            canvas.FillColor = color.WithAlpha(0.18f);
            canvas.FillCircle(cx, cy, radius * 1.35f);
        }

        canvas.FillColor = color;
        canvas.StrokeColor = color;

        switch (category)
        {
            // A mood is the plainest thing recorded here, and it gets the plainest
            // shape — a soft orb.
            case CelestialCategory.Mood:
                canvas.FillCircle(cx, cy, radius * 0.78f);
                break;

            case CelestialCategory.Weight:
                canvas.FillPath(Polygon(cx, cy, radius, 4, up));
                break;

            case CelestialCategory.Glucose:
                canvas.FillPath(Star(cx, cy, radius * 1.15f, radius * 0.3f, 4, up));
                break;

            case CelestialCategory.Appetite:
                canvas.FillPath(Polygon(cx, cy, radius, 3, up));
                break;

            case CelestialCategory.Water:
                canvas.FillPath(Crescent(cx, cy, radius));
                break;

            case CelestialCategory.Seizure:
                canvas.FillPath(Star(cx, cy, radius * 1.2f, radius * 0.44f, 8, up));
                break;

            // A ring, not a disc: a dose is the one thing here the app asked for and
            // the owner answered, so it reads as an outline being closed.
            case CelestialCategory.Medication:
                canvas.StrokeSize = MathF.Max(1f, radius * 0.36f);
                canvas.DrawCircle(cx, cy, radius * 0.72f);
                break;

            case CelestialCategory.Custom:
                canvas.FillPath(Polygon(cx, cy, radius * 0.95f, 6, up));
                break;
        }
    }

    /// <summary>A regular polygon with <paramref name="sides"/> vertices.</summary>
    private static PathF Polygon(float cx, float cy, float radius, int sides, float rotation)
    {
        var path = new PathF();
        for (int i = 0; i < sides; i++)
        {
            var angle = rotation + i * 2f * MathF.PI / sides;
            var x = cx + radius * MathF.Cos(angle);
            var y = cy + radius * MathF.Sin(angle);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }
        path.Close();
        return path;
    }

    /// <summary>A concave star: <paramref name="points"/> tips at
    /// <paramref name="outer"/>, waists at <paramref name="inner"/>. Four points read
    /// as a sparkle, eight as a burst.</summary>
    private static PathF Star(float cx, float cy, float outer, float inner, int points, float rotation)
    {
        var path = new PathF();
        var step = MathF.PI / points;

        for (int i = 0; i < points * 2; i++)
        {
            var radius = i % 2 == 0 ? outer : inner;
            var angle = rotation + i * step;
            var x = cx + radius * MathF.Cos(angle);
            var y = cy + radius * MathF.Sin(angle);
            if (i == 0)
                path.MoveTo(x, y);
            else
                path.LineTo(x, y);
        }

        path.Close();
        return path;
    }

    /// <summary>A moon. Built as one closed shape (an outer bulge and a concave back)
    /// rather than as a circle with a bite taken out of it — punching a hole would
    /// need the background colour, and there isn't one: the sky behind it is a
    /// gradient.</summary>
    private static PathF Crescent(float cx, float cy, float radius)
    {
        var path = new PathF();
        path.MoveTo(cx + radius * 0.15f, cy - radius);
        path.CurveTo(
            cx - radius * 1.25f, cy - radius * 0.55f,
            cx - radius * 1.25f, cy + radius * 0.55f,
            cx + radius * 0.15f, cy + radius);
        path.CurveTo(
            cx - radius * 0.42f, cy + radius * 0.5f,
            cx - radius * 0.42f, cy - radius * 0.5f,
            cx + radius * 0.15f, cy - radius);
        path.Close();
        return path;
    }
}

/// <summary>
/// One symbol on its own, centred and filled — the legend's cell. Eight tiny
/// GraphicsViews rather than eight images, so the legend can never fall out of step
/// with the sky and no asset has to be redrawn when a symbol changes.
/// </summary>
public sealed class CelestialSymbolDrawable : IDrawable
{
    public CelestialCategory Category { get; set; }
    public Color Color { get; set; } = Colors.White;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var radius = MathF.Min(rect.Width, rect.Height) * 0.32f;
        CelestialSymbols.Draw(canvas, Category, rect.Center.X, rect.Center.Y, radius, Color, glow: true);
    }
}
