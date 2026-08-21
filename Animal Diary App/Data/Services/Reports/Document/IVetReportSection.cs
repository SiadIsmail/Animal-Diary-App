namespace Animal_Diary_App.Data.Services.Reports.Document;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// One independent block of the report. Sections know nothing about each other,
/// about the database, or about their position in the document: each takes the
/// full DTO and appends only its own slice to the shared MigraDoc <see cref="Section"/>.
/// Reorder / remove / rewrite one without touching the rest (the order lives in
/// <see cref="VetReportDocument"/>).
/// </summary>
public interface IVetReportSection
{
    /// <summary>False = the section is omitted entirely, no empty boxes, no
    /// headings over nothing. This is how the document degrades gracefully.</summary>
    bool HasContent(VetReportData data);

    /// <summary>Append this section's content to <paramref name="section"/>. Use
    /// <paramref name="ctx"/> for chart images and the content width.</summary>
    void Compose(Section section, VetReportData data, ReportContext ctx);
}
