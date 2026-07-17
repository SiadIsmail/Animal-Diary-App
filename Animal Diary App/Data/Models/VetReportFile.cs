namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// One generated vet-report PDF in the app's report library (the Documents page).
/// A row is metadata only — the PDF and its pre-rendered preview PNGs live in the
/// reports folder owned by <c>ReportLibraryService</c>, which derives their full
/// paths from <see cref="FileName"/>. Rows and files are created/deleted together
/// by that service; a row whose file has vanished is dropped on the next read.
/// </summary>
public class VetReportFile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PetId { get; set; }

    /// <summary>PDF file name RELATIVE to the reports folder — never an absolute
    /// path, because the app-data root can move between installs/backups.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Inclusive date range the report covers.</summary>
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>How many pages the PDF has == how many preview PNGs exist.</summary>
    public int PageCount { get; set; }

    /// <summary>PDF size on disk, for the Documents row's size label.</summary>
    public long SizeBytes { get; set; }
}
