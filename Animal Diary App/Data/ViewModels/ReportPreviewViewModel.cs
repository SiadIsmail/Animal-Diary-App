namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Reports;

/// <summary>
/// Backs the in-app report preview page: the report's pre-rendered page PNGs
/// (saved next to the PDF at generation time — see <see cref="VetReportService"/>)
/// plus the two ways it leaves the app (share / open externally). Call
/// <see cref="Open"/> with a library row BEFORE pushing the page.
/// </summary>
public class ReportPreviewViewModel : BaseViewModel
{
    private VetReportFile? _report;

    public ReportPreviewViewModel()
    {
        ShareCommand = new Command(async () => await RunAsync(ReportActions.ShareAsync));
        OpenExternallyCommand = new Command(async () => await RunAsync(ReportActions.OpenExternallyAsync));
    }

    /// <summary>Absolute paths of the page images, in page order.</summary>
    public ObservableCollection<string> PageImages { get; } = new();

    /// <summary>The PDF's file name — shown as the page subtitle (data, not copy).</summary>
    public string FileName => _report?.FileName ?? string.Empty;

    public ICommand ShareCommand { get; }
    public ICommand OpenExternallyCommand { get; }

    public void Open(VetReportFile report)
    {
        _report = report;

        PageImages.Clear();
        foreach (var path in ReportLibraryService.PreviewPathsFor(report))
            PageImages.Add(path);

        OnPropertyChanged(nameof(FileName));
    }

    // Handing the PDF to the OS can fail without it being our bug (no share
    // targets, no PDF viewer installed) — log and stay put, never crash.
    private async Task RunAsync(Func<VetReportFile, Task> action)
    {
        if (_report == null)
            return;
        try
        {
            await action(_report);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] action failed: {ex}");
        }
    }
}
