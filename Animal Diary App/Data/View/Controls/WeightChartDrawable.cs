namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Graphics;

/// <summary>
/// Draws the Felova weight-trend chart: a smooth (Catmull-Rom) viola line with a
/// soft gradient area fill, faint baseline gridlines, and a highlighted latest
/// point. Pure Microsoft.Maui.Graphics — no charting dependency. The hosting
/// <see cref="WeightChartView"/> feeds it <see cref="Values"/> + axis bounds and
/// calls Invalidate whenever the series changes.
/// </summary>
public sealed class WeightChartDrawable : IDrawable
{
    public IReadOnlyList<double> Values { get; set; } = System.Array.Empty<double>();
    public double Min { get; set; }
    public double Max { get; set; }

    // Rockpool-warm palette: the weigh-in accent is Blue (#3E8FB0), matching
    // the Journal timeline's weigh-in icon. Geometry is unchanged — colours only.
    private static readonly Color LineColor = Color.FromArgb("#3E8FB0"); // blue
    private static readonly Color FillTop = Color.FromArgb("#333E8FB0"); // blue @ ~20%
    private static readonly Color FillBottom = Color.FromArgb("#0A3E8FB0");
    private static readonly Color GridColor = Color.FromArgb("#1A0D3A3C"); // ink @ ~10%
    private static readonly Color DotColor = Color.FromArgb("#3E8FB0");

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Values.Count == 0)
            return;

        const float padL = 6, padR = 6, padT = 14, padB = 10;
        float w = rect.Width - padL - padR;
        float h = rect.Height - padT - padB;
        if (w <= 0 || h <= 0)
            return;

        double range = Max - Min;
        if (range <= 0)
            range = 1;

        float X(int i) => Values.Count == 1
            ? padL + w / 2f
            : padL + w * i / (Values.Count - 1);
        float Y(double v) => padT + (float)(h * (1 - (v - Min) / range));

        // Faint baseline gridlines (top / mid / bottom).
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        for (int g = 0; g < 3; g++)
        {
            float gy = padT + h * g / 2f;
            canvas.DrawLine(padL, gy, padL + w, gy);
        }

        var pts = new List<PointF>(Values.Count);
        for (int i = 0; i < Values.Count; i++)
            pts.Add(new PointF(X(i), Y(Values[i])));

        // Single point: just a dot.
        if (pts.Count == 1)
        {
            canvas.FillColor = DotColor;
            canvas.FillCircle(pts[0].X, pts[0].Y, 4);
            return;
        }

        float bottom = padT + h;

        // Area fill under the curve.
        var fill = new PathF();
        fill.MoveTo(pts[0].X, bottom);
        fill.LineTo(pts[0].X, pts[0].Y);
        AppendSmooth(fill, pts);
        fill.LineTo(pts[^1].X, bottom);
        fill.Close();

        canvas.SetFillPaint(new LinearGradientPaint
        {
            StartColor = FillTop,
            EndColor = FillBottom,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        }, fill.Bounds);
        canvas.FillPath(fill);

        // The line itself.
        var line = new PathF();
        line.MoveTo(pts[0].X, pts[0].Y);
        AppendSmooth(line, pts);

        canvas.StrokeColor = LineColor;
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(line);

        // Highlighted latest point (white ring + viola core).
        var last = pts[^1];
        canvas.FillColor = Colors.White;
        canvas.FillCircle(last.X, last.Y, 6);
        canvas.FillColor = DotColor;
        canvas.FillCircle(last.X, last.Y, 4);
    }

    /// <summary>Appends a smooth curve through the points using Catmull-Rom
    /// segments converted to cubic beziers. Assumes the path cursor is at p[0].</summary>
    private static void AppendSmooth(PathF path, List<PointF> p)
    {
        for (int i = 0; i < p.Count - 1; i++)
        {
            var p0 = i > 0 ? p[i - 1] : p[0];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = i + 2 < p.Count ? p[i + 2] : p2;

            float c1x = p1.X + (p2.X - p0.X) / 6f;
            float c1y = p1.Y + (p2.Y - p0.Y) / 6f;
            float c2x = p2.X - (p3.X - p1.X) / 6f;
            float c2y = p2.Y - (p3.Y - p1.Y) / 6f;

            path.CurveTo(c1x, c1y, c2x, c2y, p2.X, p2.Y);
        }
    }
}
