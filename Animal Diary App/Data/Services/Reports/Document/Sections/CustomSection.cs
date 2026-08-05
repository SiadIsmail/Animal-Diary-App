namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

/// <summary>
/// Whatever the owner tracks themselves and chose to show a vet: a one-line tally, then
/// the same terse dated table <see cref="EventsSection"/> uses.
///
/// <para><b>Every label in this section is the owner's own text</b> — the tracker's name
/// and its unit — printed verbatim and never translated, exactly like a pet or medication
/// name. That is the whole reason this is not folded into <c>EventsSection</c>, whose
/// <c>ReportEventKind</c> is a closed set the document knows how to word.</para>
///
/// <para>The tally counts rows, which is a fact. Nothing here totals across the range,
/// computes a rate, or says whether any of it is a lot.</para>
/// </summary>
public class CustomSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Custom.HasContent;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var shown = data.Custom.Entries.Take(VetReportStyles.MaxEventRows).ToList();
        var older = data.Custom.Entries.Count - shown.Count;

        SectionChrome.AddTitle(section, VetReportStrings.SectionCustom);

        // The tally: "Vomiting 4× · Poop 12×". A vet reads this line and knows whether
        // the table below is worth their time.
        if (data.Custom.Trackers.Count > 0)
        {
            var tally = string.Join(" · ", data.Custom.Trackers
                .Select(t => $"{t.Name} {VetReportStrings.CustomTimes(t.Count)}"));
            var line = section.AddParagraph(tally);
            line.Format.SpaceAfter = 4;
            line.Format.Font.Size = VetReportStyles.SmallSize;
            line.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        }

        var table = section.AddTable();
        table.Borders.Width = 0;

        // Same geometry as the events table, so the two read as siblings.
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
            BodyCellBold(row, 2, e.Name);
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

    // The number with the owner's own unit, their note, or both. A Tick tracker has
    // neither, and an entry that only says "it happened" is complete as it is — the date
    // and time in the same row already carry it.
    private static string Details(ReportCustomEntry e)
    {
        var parts = new List<string>(2);

        if (e.Amount is decimal value)
        {
            var number = value.ToString("0.#", CultureInfo.CurrentCulture);
            var unit = e.Unit?.Trim() ?? string.Empty;
            parts.Add(unit.Length > 0 ? $"{number} {unit}" : number);
        }

        // The owner's own words, printed verbatim — never translated.
        if (e.Note != null)
            parts.Add($"“{e.Note}”");

        return parts.Count > 0 ? string.Join(" · ", parts) : VetReportStrings.Empty;
    }
}
