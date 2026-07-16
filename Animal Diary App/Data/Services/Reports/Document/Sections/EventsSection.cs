namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// Terse dated table of notable occurrences, newest first. The wording per
/// <see cref="ReportEventKind"/> lives here and states only what was logged —
/// severity words, causes and conclusions are the vet's job, not ours.
/// Capped at <see cref="VetReportStyles.MaxEventRows"/> rows to protect the
/// one-page target; the cap is stated so nothing looks hidden.
/// </summary>
public class EventsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Events.Count > 0;

    public void Compose(IContainer container, VetReportData data)
    {
        var shown = data.Events.Take(VetReportStyles.MaxEventRows).ToList();
        var older = data.Events.Count - shown.Count;

        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Events"));

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(64);  // date
                    columns.ConstantColumn(36);  // time
                    columns.RelativeColumn(2);   // event
                    columns.RelativeColumn(5);   // details
                });

                table.Header(header =>
                {
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Date");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Time");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Event");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Details (owner-reported)");
                });

                foreach (var e in shown)
                {
                    table.Cell().Element(SectionChrome.BodyCell).Text(e.Date.ToString(VetReportStyles.DateFormat));
                    table.Cell().Element(SectionChrome.BodyCell).Text(e.Time?.ToString(VetReportStyles.TimeFormat) ?? "—");
                    table.Cell().Element(SectionChrome.BodyCell).Text(Label(e)).SemiBold();
                    table.Cell().Element(SectionChrome.BodyCell).Text(Details(e));
                }
            });

            if (older > 0)
                col.Item().PaddingTop(2).Text($"+ {older} earlier event(s) in the period not listed.")
                    .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
        });
    }

    private static string Label(ReportEvent e) => e.Kind switch
    {
        ReportEventKind.Seizure => "Seizure",
        ReportEventKind.Vomiting => "Vomiting",
        ReportEventKind.LowAppetite => "Low appetite",
        _ => e.Kind.ToString()
    };

    private static string Details(ReportEvent e)
    {
        var parts = new List<string>();
        if (e.DurationMinutes is int min)
            parts.Add($"duration ≈ {min} min");
        if (e.Kind == ReportEventKind.LowAppetite && e.Value is int level)
            parts.Add($"owner logged appetite level {level} of 5");
        if (e.Note != null)
            parts.Add($"“{e.Note}”");
        return parts.Count > 0 ? string.Join(" · ", parts) : "—";
    }
}
