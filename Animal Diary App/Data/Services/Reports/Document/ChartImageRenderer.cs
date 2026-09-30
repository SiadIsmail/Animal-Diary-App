namespace Animal_Diary_App.Data.Services.Reports.Document;

using SkiaSharp;

/// <summary>
/// Turns a chart drawing (see <see cref="ChartRenderer"/> /
/// <see cref="ObservationChartRenderer"/>) into a PNG byte array that MigraDoc embeds
/// as an <c>Image</c>. QuestPDF used to hand the renderers a live in-PDF canvas; now we
/// draw onto an offscreen SkiaSharp surface and rasterize.
///
/// The renderers work in PDF points; we supersample by <see cref="Scale"/> so the
/// embedded raster stays crisp at print DPI, then MigraDoc scales the image back to the
/// requested point size.
/// </summary>
public static class ChartImageRenderer
{
    private const int Scale = 3;

    // Widths/heights arrive as double (content width is computed in points); the Skia
    // renderers work in float, so cast once here.
    public static byte[] Render(double widthPt, double heightPt, Action<SKCanvas, float, float> draw)
    {
        var w = (float)widthPt;
        var h = (float)heightPt;
        var wPx = (int)MathF.Ceiling(w * Scale);
        var hPx = (int)MathF.Ceiling(h * Scale);

        using var surface = SKSurface.Create(new SKImageInfo(wPx, hPx, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);   // the report page is white; keep the chart on white
        canvas.Scale(Scale);            // draw in point space, output at Scale×
        draw(canvas, w, h);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static byte[] Line(double widthPt, double heightPt, ReportSeries series) =>
        Render(widthPt, heightPt, (c, w, h) => ChartRenderer.Draw(c, w, h, series));

    public static byte[] Observation(
        double widthPt, double heightPt,
        IReadOnlyList<ReportObservation> observations, IReadOnlyList<string> rowLabels) =>
        Render(widthPt, heightPt, (c, w, h) => ObservationChartRenderer.Draw(c, w, h, observations, rowLabels));
}
