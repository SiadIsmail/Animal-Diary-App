namespace Animal_Diary_App.Data.Services.Reports;

/// <summary>
/// Fallback for platforms without a wired OS PDF renderer (currently iOS/macOS): the PDF
/// still generates and the page count is still recorded, there are simply no preview
/// PNGs. The Documents viewer degrades gracefully — <see cref="ReportLibraryService.PreviewPathsFor"/>
/// skips missing files — so the report can still be viewed externally and shared.
/// </summary>
public sealed class NoOpPdfPageRasterizer : IPdfPageRasterizer
{
    public Task RasterizeAsync(string pdfPath, int pageCount, Func<int, string> pathForPage, int dpi) =>
        Task.CompletedTask;
}
