namespace Animal_Diary_App.Data.Services.Reports.Document;

using QuestPDF.Infrastructure;

/// <summary>
/// One independent block of the report. Sections know nothing about each other,
/// about the database, or about their position in the document — each takes the
/// full DTO and renders only its own slice. Reorder / remove / rewrite one
/// without touching the rest (the order lives in <see cref="VetReportDocument"/>).
/// </summary>
public interface IVetReportSection
{
    /// <summary>False = the section is omitted entirely — no empty boxes, no
    /// headings over nothing. This is how the document degrades gracefully.</summary>
    bool HasContent(VetReportData data);

    void Compose(IContainer container, VetReportData data);
}
