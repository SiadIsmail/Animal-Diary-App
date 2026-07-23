namespace Animal_Diary_App.Data.Services.Reports.Document;

using SkiaSharp;

/// <summary>
/// Draws a QUALITATIVE observation chart: a dot per dated observation, sitting on a
/// word-labelled category row. This is deliberately NOT the numeric line chart —
/// owner observations ("Normal", "More than usual") are subjective, so:
/// <list type="bullet">
/// <item>the y-axis shows WORDS, never numbers or a scale;</item>
/// <item>dots are never joined by a line (no interpolation, no implied trend);</item>
/// <item>nothing is averaged, summed, or given a verdict.</item>
/// </list>
/// The report relays what the owner observed; the vet interprets it. Grayscale-safe
/// like <see cref="ChartRenderer"/> (black dots, grey gridlines).
/// </summary>
public static class ObservationChartRenderer
{
    private const float Left = 58f, Right = 6f, Top = 6f, Bottom = 13f;

    /// <param name="rowLabels">Category words, bottom row (level 1) first to top row
    /// (level N last). The dot for an observation of level L sits on row L−1.</param>
    public static void Draw(
        SKCanvas canvas, float width, float height,
        IReadOnlyList<ReportObservation> observations, IReadOnlyList<string> rowLabels)
    {
        if (observations.Count == 0 || rowLabels.Count == 0)
            return;

        var plotW = width - Left - Right;
        var plotH = height - Top - Bottom;
        var rows = rowLabels.Count;

        // ── Date scale (x) ──────────────────────────────────────────────────
        var ordered = observations.OrderBy(o => o.Date).ToList();
        var minDate = ordered[0].Date;
        var maxDate = ordered[^1].Date;
        var dateSpan = Math.Max((maxDate - minDate).TotalDays, 1); // 1 avoids ÷0 for a single day

        float X(System.DateTime d) => Left + (float)((d - minDate).TotalDays / dateSpan) * plotW;
        // Category row (y): level 1 at the bottom, the top level at the top. rows==1
        // would divide by zero, so pin a lone category to the middle.
        float RowY(int level)
        {
            var idx = Math.Clamp(level - 1, 0, rows - 1);
            var frac = rows > 1 ? idx / (float)(rows - 1) : 0.5f;
            return Top + (1 - frac) * plotH;
        }

        // ── Paints ──────────────────────────────────────────────────────────
        using var gridPaint = new SKPaint { Color = SKColor.Parse(VetReportStyles.ChartGrid), StrokeWidth = 0.5f, IsAntialias = true };
        using var markerFill = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var labelPaint = new SKPaint { Color = SKColor.Parse(VetReportStyles.InkTertiary), TextSize = VetReportStyles.ChartLabelSize, IsAntialias = true };
        using var labelRight = new SKPaint { Color = SKColor.Parse(VetReportStyles.InkTertiary), TextSize = VetReportStyles.ChartLabelSize, IsAntialias = true, TextAlign = SKTextAlign.Right };

        // ── Category rows: a gridline + its word label ──────────────────────
        for (var level = 1; level <= rows; level++)
        {
            var y = RowY(level);
            canvas.DrawLine(Left, y, width - Right, y, gridPaint);
            canvas.DrawText(rowLabels[level - 1], Left - 3, y + VetReportStyles.ChartLabelSize / 2 - 1, labelRight);
        }

        // ── Date labels: first / middle / last ──────────────────────────────
        var midDate = minDate.AddDays(dateSpan / 2);
        var labelY = height - 2;
        canvas.DrawText(minDate.ToString(VetReportStyles.ShortDateFormat), Left, labelY, labelPaint);
        using (var centered = labelPaint.Clone())
        {
            centered.TextAlign = SKTextAlign.Center;
            canvas.DrawText(midDate.ToString(VetReportStyles.ShortDateFormat), Left + plotW / 2, labelY, centered);
        }
        canvas.DrawText(maxDate.ToString(VetReportStyles.ShortDateFormat), width - Right, labelY, labelRight);

        // ── The observations: one dot each, never joined ───────────────────
        foreach (var o in ordered)
            canvas.DrawCircle(X(o.Date), RowY(o.Level), VetReportStyles.ChartMarkerRadius + 0.4f, markerFill);
    }
}
