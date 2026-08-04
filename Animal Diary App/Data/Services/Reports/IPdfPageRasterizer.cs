namespace Animal_Diary_App.Data.Services.Reports;

/// <summary>
/// Renders each page of a generated PDF to a PNG file for the in-app preview (the
/// Documents viewer shows those images because Android WebView can't display a PDF).
/// QuestPDF used to do this via its Skia pipeline; MigraDoc/PDFsharp can't rasterize,
/// so each platform uses its own OS PDF renderer — none of which adds a native library.
/// Implementations are best-effort per page: a page that fails to render is skipped,
/// never fatal (the viewer already tolerates missing preview files).
/// </summary>
public interface IPdfPageRasterizer
{
    /// <param name="pdfPath">The PDF just written to disk.</param>
    /// <param name="pageCount">Pages to render (from the PDF itself).</param>
    /// <param name="pathForPage">Given the 1-based page number, the PNG output path.</param>
    /// <param name="dpi">Raster resolution.</param>
    Task RasterizeAsync(string pdfPath, int pageCount, Func<int, string> pathForPage, int dpi);
}
