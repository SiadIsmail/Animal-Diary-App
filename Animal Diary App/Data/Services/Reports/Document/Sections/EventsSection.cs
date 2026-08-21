namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

/// <summary>
/// Terse dated table of notable occurrences, newest first. The wording per
/// <see cref="ReportEventKind"/> lives here and states only what was logged,
/// severity words, causes and conclusions are the vet's job, not ours.
/// Capped at <see cref="VetReportStyles.MaxEventRows"/> rows to protect the
/// one-page target; the cap is stated so nothing looks hidden.
/// </summary>
public class EventsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Events.Count > 0;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var shown = data.Events.Take(VetReportStyles.MaxEventRows).ToList();
        var older = data.Events.Count - shown.Count;

        SectionChrome.AddTitle(section, VetReportStrings.SectionEvents);

        var table = section.AddTable();
        table.Borders.Width = 0;

        // date 64 · time 36 (fixed) · event 2 · details 5 (of the remaining width)
        const double dateW = 64, timeW = 36;
        var rest = ctx.ContentWidthPt - dateW - timeW;
        table.AddColumn(Unit.FromPoint(dateW));
        table.AddColumn(Unit.FromPoint(timeW));
        table.AddColumn(Unit.FromPoint(rest * 2 / 7));
        table.AddColumn(Unit.FromPoint(rest * 5 / 7));

        var head = table.AddRow();
        head.HeadingFormat = true;
        HeaderCell(head, 0, VetReportStrings.ColDate);
        HeaderCell(head, 1, VetReportStrings.ColTime);
        HeaderCell(head, 2, VetReportStrings.ColEvent);
        HeaderCell(head, 3, VetReportStrings.ColDetails);

        foreach (var e in shown)
        {
            var row = table.AddRow();
            BodyCell(row, 0, e.Date.ToString(VetReportStyles.DateFormat));
            BodyCell(row, 1, e.Time?.ToString(VetReportStyles.TimeFormat) ?? VetReportStrings.Empty);
            BodyCellBold(row, 2, Label(e));
            BodyCell(row, 3, Details(e));
        }

        if (older > 0)
        {
            var p = section.AddParagraph(VetReportStrings.MoreEvents(older));
            p.Format.SpaceBefore = 2;
            p.Format.Font.Size = VetReportStyles.SmallSize;
            p.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        }
    }

    private static void HeaderCell(Row row, int i, string text)
    {
        SectionChrome.ConfigureHeaderCell(row.Cells[i]);
        row.Cells[i].AddParagraph(text);
    }

    private static void BodyCell(Row row, int i, string text)
    {
        SectionChrome.ConfigureBodyCell(row.Cells[i]);
        row.Cells[i].AddParagraph(text);
    }

    private static void BodyCellBold(Row row, int i, string text)
    {
        SectionChrome.ConfigureBodyCell(row.Cells[i]);
        row.Cells[i].AddParagraph().AddFormattedText(text, TextFormat.Bold);
    }

    private static string Label(ReportEvent e) => e.Kind switch
    {
        ReportEventKind.Seizure => VetReportStrings.EventSeizure,
        ReportEventKind.Vomiting => VetReportStrings.EventVomiting,
        ReportEventKind.LowAppetite => VetReportStrings.EventLowAppetite,
        _ => e.Kind.ToString()
    };

    private static string Details(ReportEvent e)
    {
        var parts = new List<string>();
        // First: it is the most clinically legible thing in the row, and it is the
        // owner's own answer: absent when they didn't give one.
        if (e.SeizureType is not null)
            parts.Add(VetReportStrings.SeizureType(e.SeizureType));
        if (e.DurationMinutes is int min)
            parts.Add(VetReportStrings.EventDuration(min));
        if (e.Kind == ReportEventKind.LowAppetite && e.Value is int level)
            parts.Add(VetReportStrings.EventAppetiteLevel(level));
        // The owner's own words, printed verbatim, never translated.
        if (e.Note != null)
            parts.Add($"“{e.Note}”");
        return parts.Count > 0 ? string.Join(" · ", parts) : VetReportStrings.Empty;
    }
}
