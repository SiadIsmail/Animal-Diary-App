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
    /// the library row. Returns null when the range holds no loggable data at all,
    /// no empty documents are ever produced.
    /// <paramref name="includePhoto"/> is opt-in (default off, matching the report's
    /// data-minimized ethos): the pet's profile photo appears in the header only when
    /// the owner ticks it AND a photo file exists.
    /// The <c>include…Measured</c> / <c>include…Observations</c> pairs (all default ON)
    /// independently include a metric's two data types: objective measurements and
    /// subjective observations, which the report always keeps separate and never
    /// interprets. Today: water (mL) and appetite (grams).
    /// <paramref name="includeMood"/> (default ON) covers the daily mood readings, which
    /// are observations only: there is no measured counterpart to pair it with.
    /// <paramref name="includeCustom"/> (default ON) is the whole owner-defined section.
    /// It is deliberately ONE toggle rather than one per tracker: whether a walk belongs
    /// in front of a vet is a property of the tracker, answered once on the tracker itself
    /// (<c>CustomTracker.IncludeInReport</c>), so trackers switched off there never reach
    /// the builder at all. This flag is only the usual per-export escape hatch.</summary>
    Task<VetReportFile?> GenerateAsync(
        int petId, DateTime from, DateTime to,
        bool includePhoto = false,
        bool includeWaterMeasured = true,
        bool includeWaterObservations = true,
        bool includeAppetiteMeasured = true,
        bool includeAppetiteObservations = true,
        bool includeMood = true,
        bool includeCustom = true);

    /// <summary>
    /// Generate the PLAIN export: everything the owner wrote down in the range, in time
    /// order, with dates and times. Same return shape as <see cref="GenerateAsync"/>,
    /// it lands in the report library like any other document, and null when the range
    /// holds nothing.
    ///
    /// <para><b>Free forever, on every tier.</b> This is the method that keeps
    /// "getting your data out is never blocked" true (AI/domain.md). No caller may gate
    /// it, and it takes no include/exclude flags on purpose: an export that promises
    /// everything is not something to configure your way out of. The designed report is
    /// the paid artifact; this is the data.</para>
    /// </summary>
    Task<VetReportFile?> GeneratePlainAsync(int petId, DateTime from, DateTime to);

    /// <summary>Generate a PDF from the fake <see cref="VetReportSampleData"/>: for
    /// iterating on the layout without real logged data. The files land in the
    /// reports folder but the returned row is NOT persisted, so sample documents
    /// never appear in the Documents list.</summary>
    Task<VetReportFile> GenerateSampleAsync();
}
