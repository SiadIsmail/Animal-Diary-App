namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Reports.Document;
using MigraDoc.Rendering;

/// <summary>
/// Orchestrates the three report layers: DATA (builder) → DOCUMENT (MigraDoc sections)
/// → files on disk, registered in the <see cref="ReportLibraryService"/>. Entirely
/// local/offline; nothing leaves the device.
///
/// The document is built with PDFsharp/MigraDoc (pure managed, no native libraries — it
/// replaced QuestPDF, whose SkiaSharp pin broke 16 KB-page-size compliance). Each export
/// also writes one PNG per page ("{name}.p{n}.png") via the platform
/// <see cref="IPdfPageRasterizer"/>: the in-app preview shows those images because Android
/// WebView can't render PDFs and MigraDoc/PDFsharp can't rasterize one.
/// </summary>
public class VetReportService : IVetReportService
{
    private readonly VetReportDataBuilder _builder;
    private readonly ReportLibraryService _library;
    private readonly IPdfPageRasterizer _rasterizer;

    public VetReportService(VetReportDataBuilder builder, ReportLibraryService library, IPdfPageRasterizer rasterizer)
    {
        _builder = builder;
        _library = library;
        _rasterizer = rasterizer;
    }

    public async Task<VetReportFile?> GenerateAsync(
        int petId, DateTime from, DateTime to,
        bool includePhoto = false,
        bool includeWaterMeasured = true,
        bool includeWaterObservations = true,
        bool includeAppetiteMeasured = true,
        bool includeAppetiteObservations = true,
        bool includeMood = true,
        bool includeCustom = true)
    {
        var data = await _builder.BuildAsync(
            petId, from, to, includePhoto,
            includeWaterMeasured, includeWaterObservations,
            includeAppetiteMeasured, includeAppetiteObservations,
            includeMood, includeCustom);
        if (!data.HasAnyData)
            return null;

        var report = await SaveAsync(data, petId);
        return await _library.AddAsync(report);
    }

    public async Task<VetReportFile?> GeneratePlainAsync(int petId, DateTime from, DateTime to)
    {
        var data = await _builder.BuildPlainAsync(petId, from, to);
        if (!data.HasAnyData)
            return null;

        var report = await SaveAsync(data, petId);
        return await _library.AddAsync(report);
    }

    // Sample documents are written to disk (so View/Share work) but the row is never
    // inserted — Id stays 0, Documents never lists it.
    //
    // Only the ONE-PAGE fixture is left. The full 90-day layout-stress fixture is gone,
    // replaced by exporting a seeded demo pet: that runs the real builder over real rows,
    // so it exercises page breaks and the continuation header more honestly than a
    // hand-written VetReportData ever did. This one survives because it does a job the
    // demo pets cannot — a store screenshot needs a report that ends on page one, and
    // that is bought by carrying less, not by shrinking type.
    public Task<VetReportFile> GenerateSampleAsync() =>
        SaveAsync(VetReportSampleData.CreateCompact(), petId: 0);

    private async Task<VetReportFile> SaveAsync(VetReportData data, int petId)
    {
        var fileName = UniqueFileName(data.Pet.Name, data.GeneratedAt, data.Style);
        var report = new VetReportFile
        {
            PetId = petId,
            FileName = fileName,
            FromDate = data.From,
            ToDate = data.To,
            CreatedAt = data.GeneratedAt,
        };

        var pdfPath = ReportLibraryService.PdfPathFor(report);

        // PDFsharp's font resolver is synchronous during rendering, so the bundled TTFs
        // must be loaded and installed first.
        await ReportFontResolver.EnsureRegisteredAsync();

        // ctx owns the temporary chart PNGs; it must stay alive until the PDF is rendered
        // (MigraDoc reads embedded images from disk at render time).
        using var ctx = new ReportContext(VetReportDocument.ContentWidthPt);

        // Layout + render is pure CPU work; keep it off the UI thread.
        var pageCount = await Task.Run(() =>
        {
            var document = new VetReportDocument(data).Build(ctx);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            // Read the page count BEFORE saving: PdfDocument.Save() is terminal — it
            // finalizes the in-memory document, after which PageCount (and any other
            // access) throws "document was already saved and cannot be modified".
            var pageCount = renderer.PdfDocument.PageCount;
            renderer.PdfDocument.Save(pdfPath);
            return pageCount;
        });
        report.PageCount = pageCount;

        // One PNG per page next to the PDF, for the in-app preview.
        await _rasterizer.RasterizeAsync(
            pdfPath, pageCount,
            page => ReportLibraryService.PreviewPathFor(report, page),
            VetReportStyles.PreviewRasterDpi);

        report.SizeBytes = new FileInfo(pdfPath).Length;

        System.Diagnostics.Debug.WriteLine($"[VetReport] saved: {pdfPath} ({report.PageCount} page(s))");
        return report;
    }

    /// <summary>"{Pet}_Felova_{date_time}.pdf", de-duplicated with a numeric suffix —
    /// re-exports must never overwrite an earlier report in the library. The plain export
    /// carries "_log" so the two are told apart in a file manager and in an email
    /// attachment list, where the app's own labels are not there to help.</summary>
    private static string UniqueFileName(string petName, DateTime createdAt, ReportStyle style)
    {
        var kind = style == ReportStyle.Plain ? "_log" : string.Empty;
        var baseName = $"{SanitizeFileName(petName)}_Felova{kind}_{createdAt:yyyy-MM-dd_HHmm}";
        var fileName = baseName + ".pdf";
        for (var n = 2; File.Exists(Path.Combine(ReportLibraryService.ReportsDirectory, fileName)); n++)
            fileName = $"{baseName}_{n}.pdf";
        return fileName;
    }

    /// <summary>Pet names are free text — strip anything a filesystem would reject.</summary>
    private static string SanitizeFileName(string name)
    {
        var cleaned = new string(name.Trim()
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Pet" : cleaned;
    }
}
