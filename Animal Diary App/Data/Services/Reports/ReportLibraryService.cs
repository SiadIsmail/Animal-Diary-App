namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Owns the report library: the <c>Reports/</c> folder inside app storage and the
/// <see cref="VetReportFile"/> metadata table. Everything that touches report
/// files on disk goes through here — the generator registers new exports, the
/// Documents page lists/deletes them, and the app reset wipes them. Rows and
/// files live and die together; a row whose PDF has vanished is dropped on read.
/// </summary>
public class ReportLibraryService
{
    private readonly SQLiteAsyncConnection _db;

    public ReportLibraryService(AppDatabase database)
    {
        _db = database.Connection;
    }

    /// <summary>The folder every report PDF + preview PNG lives in. Created lazily
    /// so a fresh install has no empty directory until the first export.</summary>
    public static string ReportsDirectory
    {
        get
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "Reports");
            Directory.CreateDirectory(dir); // no-op when it already exists
            return dir;
        }
    }

    /// <summary>Absolute path of a report's PDF.</summary>
    public static string PdfPathFor(VetReportFile report) =>
        Path.Combine(ReportsDirectory, report.FileName);

    /// <summary>Absolute path of one pre-rendered preview page (1-based). Preview
    /// PNGs sit next to the PDF as "{name}.p{page}.png".</summary>
    public static string PreviewPathFor(VetReportFile report, int page) =>
        Path.Combine(ReportsDirectory,
            $"{Path.GetFileNameWithoutExtension(report.FileName)}.p{page}.png");

    /// <summary>All preview-page paths of a report, in page order, skipping any
    /// that are missing on disk (a partially-written preview degrades, never crashes).</summary>
    public static IReadOnlyList<string> PreviewPathsFor(VetReportFile report) =>
        Enumerable.Range(1, Math.Max(report.PageCount, 0))
            .Select(p => PreviewPathFor(report, p))
            .Where(File.Exists)
            .ToList();

    /// <summary>Record a freshly generated export. The files must already be on disk.</summary>
    public async Task<VetReportFile> AddAsync(VetReportFile report)
    {
        await _db.InsertAsync(report);
        return report;
    }

    /// <summary>The pet's reports, newest first. Reconciles on read: rows whose PDF
    /// no longer exists (cleared storage, OS cleanup) are deleted and not returned.</summary>
    public async Task<List<VetReportFile>> GetForPetAsync(int petId)
    {
        var rows = await _db.Table<VetReportFile>()
            .Where(r => r.PetId == petId)
            .ToListAsync();

        var alive = new List<VetReportFile>();
        foreach (var row in rows)
        {
            if (File.Exists(PdfPathFor(row)))
                alive.Add(row);
            else
                await _db.DeleteAsync(row);
        }

        return alive.OrderByDescending(r => r.CreatedAt).ToList();
    }

    /// <summary>Delete one report: its PDF, its preview PNGs, and its row.</summary>
    public async Task DeleteAsync(VetReportFile report)
    {
        DeleteFiles(report);
        if (report.Id != 0)
            await _db.DeleteAsync(report);
    }

    /// <summary>The app-reset path: wipe every row AND every file in the reports
    /// folder (including strays with no row — reset must leave nothing behind).</summary>
    public async Task DeleteAllAsync()
    {
        await _db.DeleteAllAsync<VetReportFile>();
        foreach (var file in Directory.EnumerateFiles(ReportsDirectory))
            TryDelete(file);
    }

    private static void DeleteFiles(VetReportFile report)
    {
        TryDelete(PdfPathFor(report));
        for (var page = 1; page <= Math.Max(report.PageCount, 1); page++)
            TryDelete(PreviewPathFor(report, page));
    }

    // Deleting files is best-effort: a locked/missing file must never take the
    // delete flow down; the read-side reconciliation mops up any leftover row.
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReportLibrary] could not delete {path}: {ex.Message}");
        }
    }
}
