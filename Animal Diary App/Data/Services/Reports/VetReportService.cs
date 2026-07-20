namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Reports.Document;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// Orchestrates the three report layers: DATA (builder) → DOCUMENT (sections) →
/// files on disk, registered in the <see cref="ReportLibraryService"/>. Entirely
/// local/offline; nothing leaves the device.
///
/// Each export produces the PDF plus one PNG per page ("{name}.p{n}.png"): the
/// in-app preview shows those images because Android WebView can't render PDFs
/// and QuestPDF can't re-read a saved one — rendering at generation time is the
/// only moment the document object exists.
/// </summary>
public class VetReportService : IVetReportService
{
    static VetReportService()
    {
        // QuestPDF Community license: free while annual gross revenue is under
        // $1M USD (https://www.questpdf.com/license/). Declared once, here.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly VetReportDataBuilder _builder;
    private readonly ReportLibraryService _library;

    public VetReportService(VetReportDataBuilder builder, ReportLibraryService library)
    {
        _builder = builder;
        _library = library;
    }

    public async Task<VetReportFile?> GenerateAsync(int petId, DateTime from, DateTime to)
    {
        var data = await _builder.BuildAsync(petId, from, to);
        if (!data.HasAnyData)
            return null;

        var report = await SaveAsync(data, petId);
        return await _library.AddAsync(report);
    }

    // Sample documents are written to disk (so View/Share work while iterating on
    // the layout) but the row is never inserted — Id stays 0, Documents never lists it.
    public Task<VetReportFile> GenerateSampleAsync() =>
        SaveAsync(VetReportSampleData.Create(), petId: 0);

    private static async Task<VetReportFile> SaveAsync(VetReportData data, int petId)
    {
        var fileName = UniqueFileName(data.Pet.Name, data.GeneratedAt);
        var report = new VetReportFile
        {
            PetId = petId,
            FileName = fileName,
            FromDate = data.From,
            ToDate = data.To,
            CreatedAt = data.GeneratedAt,
        };

        var pdfPath = ReportLibraryService.PdfPathFor(report);

        // PDF + preview rasterization are pure CPU work; keep them off the UI thread.
        await Task.Run(() =>
        {
            var document = new VetReportDocument(data);
            document.GeneratePdf(pdfPath);

            // One PNG per page next to the PDF. The delegate's index is 0-based;
            // preview files are 1-based (".p1.png" is the thumbnail).
            var pages = 0;
            document.GenerateImages(
                index => { pages = index + 1; return ReportLibraryService.PreviewPathFor(report, index + 1); },
                new ImageGenerationSettings { RasterDpi = VetReportStyles.PreviewRasterDpi });
            report.PageCount = pages;
        });

        report.SizeBytes = new FileInfo(pdfPath).Length;

        System.Diagnostics.Debug.WriteLine($"[VetReport] saved: {pdfPath} ({report.PageCount} page(s))");
        return report;
    }

    /// <summary>"{Pet}_Felova_{date_time}.pdf", de-duplicated with a numeric suffix —
    /// re-exports must never overwrite an earlier report in the library.</summary>
    private static string UniqueFileName(string petName, DateTime createdAt)
    {
        var baseName = $"{SanitizeFileName(petName)}_Felova_{createdAt:yyyy-MM-dd_HHmm}";
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
