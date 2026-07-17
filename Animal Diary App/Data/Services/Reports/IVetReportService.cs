namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The one thing the ViewModels know about vet reports. Everything behind it
/// (data snapshot → PDF layout → files) is swappable without touching callers.
/// </summary>
public interface IVetReportService
{
    /// <summary>Generate the PDF summary for a pet over an inclusive date range,
    /// save it (plus its preview page images) into the report library, and return
    /// the library row. Returns null when the range holds no loggable data at all —
    /// no empty documents are ever produced.</summary>
    Task<VetReportFile?> GenerateAsync(int petId, DateTime from, DateTime to);

    /// <summary>Generate a PDF from the fake <see cref="VetReportSampleData"/> — for
    /// iterating on the layout without real logged data. The files land in the
    /// reports folder but the returned row is NOT persisted, so sample documents
    /// never appear in the Documents list.</summary>
    Task<VetReportFile> GenerateSampleAsync();
}
