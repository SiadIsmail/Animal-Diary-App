namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The two ways a report leaves the app, shared by every surface that offers
/// them (export sheet, preview page, Documents rows) so behaviour never drifts.
/// Both hand the PDF to the OS via MAUI Essentials, whose bundled FileProvider
/// makes private app-storage files shareable on Android — no permissions needed.
/// </summary>
public static class ReportActions
{
    /// <summary>Open the OS share sheet with the report's PDF.</summary>
    public static Task ShareAsync(VetReportFile report) =>
        Share.RequestAsync(new ShareFileRequest
        {
            // The file name is the visible subject line; it's data (pet name +
            // date), not UI copy, so it is deliberately not localized.
            Title = report.FileName,
            File = new ShareFile(ReportLibraryService.PdfPathFor(report))
        });

    /// <summary>Hand the PDF to the device's default PDF viewer ("Open with…").</summary>
    public static Task OpenExternallyAsync(VetReportFile report) =>
        Launcher.OpenAsync(new OpenFileRequest
        {
            Title = report.FileName,
            File = new ReadOnlyFile(ReportLibraryService.PdfPathFor(report))
        });
}
