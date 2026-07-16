namespace Animal_Diary_App.Data.Services.Reports;

/// <summary>
/// The one thing the ViewModel knows about vet reports. Everything behind it
/// (data snapshot → PDF layout → file) is swappable without touching callers.
/// </summary>
public interface IVetReportService
{
    /// <summary>Generate the PDF summary for a pet over an inclusive date range and
    /// save it to app storage. Returns the full file path, or null when the range
    /// holds no loggable data at all (no empty documents are ever produced).</summary>
    Task<string?> GenerateAsync(int petId, DateTime from, DateTime to);

    /// <summary>Generate the PDF from the fake <see cref="VetReportSampleData"/> —
    /// for iterating on the layout without real logged data on the device.</summary>
    Task<string> GenerateSampleAsync();
}
