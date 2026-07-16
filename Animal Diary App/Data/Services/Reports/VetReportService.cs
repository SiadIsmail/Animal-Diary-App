namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Services.Reports.Document;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// Orchestrates the three report layers: DATA (builder) → DOCUMENT (sections) →
/// file on disk. Entirely local/offline; nothing leaves the device.
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

    public VetReportService(VetReportDataBuilder builder)
    {
        _builder = builder;
    }

    public async Task<string?> GenerateAsync(int petId, DateTime from, DateTime to)
    {
        var data = await _builder.BuildAsync(petId, from, to);
        if (!data.HasAnyData)
            return null;

        return await SaveAsync(data);
    }

    public Task<string> GenerateSampleAsync() => SaveAsync(VetReportSampleData.Create());

    private static async Task<string> SaveAsync(VetReportData data)
    {
        // App-local storage only in v1 — no permissions, no MediaStore. Pull the
        // file off the device via the path this returns (it's also debug-logged).
        var fileName = $"{SanitizeFileName(data.Pet.Name)}_Felova_{DateTime.Now:yyyy-MM-dd}.pdf";
        var path = Path.Combine(FileSystem.AppDataDirectory, fileName);

        // PDF composition is pure CPU work; keep it off the UI thread.
        await Task.Run(() => new VetReportDocument(data).GeneratePdf(path));

        System.Diagnostics.Debug.WriteLine($"[VetReport] saved: {path}");
        return path;
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
