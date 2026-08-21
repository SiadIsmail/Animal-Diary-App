namespace Animal_Diary_App.Data.Services.Reports.Document;

using SkiaSharp;

/// <summary>
/// Draws one small time-series line chart onto a SkiaSharp canvas (see
/// <see cref="ChartImageRenderer"/>, which hands us an offscreen one and turns the
/// result into a PNG for MigraDoc to embed). Deliberately plain and grayscale-safe:
/// black line, white-filled markers, light grey gridlines: meaning never depends on
/// colour. Coordinates are PDF points; the caller decides width/height.
/// </summary>
public static class ChartRenderer
{
    // Inner margins that make room for the value / date labels.
    private const float Left = 32f, Right = 6f, Top = 5f, Bottom = 13f;

    public static void Draw(SKCanvas canvas, float width, float height, ReportSeries series)
    {
        if (series.Points.Count == 0)
            return;

        var plotW = width - Left - Right;
        var plotH = height - Top - Bottom;

        // ── Scales ──────────────────────────────────────────────────────────
        var minDate = series.Points[0].Date;
        var maxDate = series.Points[^1].Date;
        var dateSpan = Math.Max((maxDate - minDate).TotalDays, 1); // 1 avoids ÷0 for a single day

        var values = series.Points.Select(p => (float)p.Value).ToList();
        var minV = values.Min();
        var maxV = values.Max();
        if (Math.Abs(maxV - minV) < 0.001f) { minV -= 1; maxV += 1; } // flat series still gets a band
        var pad = (maxV - minV) * 0.08f;
        minV -= pad;
        maxV += pad;
        // A series that never goes negative (weights, counts) never gets a
        // negative axis: padding must not invent values below zero.
        if (minV < 0 && values.Min() >= 0)
            minV = 0;

        float X(DateTime d) => Left + (float)((d - minDate).TotalDays / dateSpan) * plotW;
        float Y(float v) => Top + (1 - (v - minV) / (maxV - minV)) * plotH;

        // ── Paints / font (SkiaSharp 3.x: text size + alignment live on SKFont;
        //     stroke vs fill lives on SKPaint.Style) ───────────────────────────
        using var gridPaint = new SKPaint { Color = SKColor.Parse(VetReportStyles.ChartGrid), StrokeWidth = 0.5f, IsAntialias = true };
        using var linePaint = new SKPaint { Color = SKColors.Black, StrokeWidth = VetReportStyles.ChartLineWidth, Style = SKPaintStyle.Stroke, IsAntialias = true };
        using var markerFill = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var markerStroke = new SKPaint { Color = SKColors.Black, StrokeWidth = 0.8f, Style = SKPaintStyle.Stroke, IsAntialias = true };
        using var labelPaint = new SKPaint { Color = SKColor.Parse(VetReportStyles.InkTertiary), IsAntialias = true };
        using var labelFont = new SKFont(SKTypeface.Default, VetReportStyles.ChartLabelSize) { Edging = SKFontEdging.Antialias };

        // ── Gridlines + value labels (bottom / middle / top of the band) ────
        foreach (var frac in new[] { 0f, 0.5f, 1f })
        {
            var v = minV + frac * (maxV - minV);
            var y = Y(v);
            canvas.DrawLine(Left, y, width - Right, y, gridPaint);
            canvas.DrawText(v.ToString("0.#"), Left - 3, y + VetReportStyles.ChartLabelSize / 2 - 1, SKTextAlign.Right, labelFont, labelPaint);
        }

        // ── Date labels: first / middle / last ──────────────────────────────
        var midDate = minDate.AddDays(dateSpan / 2);
        var labelY = height - 2;
        canvas.DrawText(minDate.ToString(VetReportStyles.ShortDateFormat), Left, labelY, SKTextAlign.Left, labelFont, labelPaint);
        canvas.DrawText(midDate.ToString(VetReportStyles.ShortDateFormat), Left + plotW / 2, labelY, SKTextAlign.Center, labelFont, labelPaint);
        canvas.DrawText(maxDate.ToString(VetReportStyles.ShortDateFormat), width - Right, labelY, SKTextAlign.Right, labelFont, labelPaint);

        // ── The data itself: polyline + a marker on every reading ───────────
        using var path = new SKPath();
        for (var i = 0; i < series.Points.Count; i++)
        {
            var pt = new SKPoint(X(series.Points[i].Date), Y((float)series.Points[i].Value));
            if (i == 0) path.MoveTo(pt); else path.LineTo(pt);
        }
        canvas.DrawPath(path, linePaint);

        foreach (var p in series.Points)
        {
            var x = X(p.Date);
            var y = Y((float)p.Value);
            canvas.DrawCircle(x, y, VetReportStyles.ChartMarkerRadius, markerFill);
            canvas.DrawCircle(x, y, VetReportStyles.ChartMarkerRadius, markerStroke);
        }
    }
}
