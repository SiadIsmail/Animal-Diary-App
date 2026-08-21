namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

/// <summary>
/// The whole plain export: everything the owner wrote down, one line each, oldest
/// first. It is the ONLY section the plain document carries besides the header.
///
/// <para><b>What it deliberately does not do.</b> No grouping, no totals, no counts, no
/// charts, no measured-vs-observed split, no caps. Those are the designed report's work
/// ON the data, and they are the paid half of the line. This one is the data: dates,
/// times, what was recorded, and what it said. That is what makes it a portable copy
/// rather than a preview of something better.</para>
///
/// <para><b>No row cap, and that is the point.</b> Every other section truncates
/// (<c>MaxEventRows</c>, <c>MaxNotes</c>) to keep the handed-over document short. An
/// export that promises "everything you wrote down" and quietly drops the oldest half is
/// not portability, so this one runs as long as it runs.</para>
/// </summary>
public class PlainLogSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.PlainLog.Count > 0;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        SectionChrome.AddTitle(section, VetReportStrings.SectionPlainLog);

        var intro = section.AddParagraph(VetReportStrings.PlainLogIntro);
        intro.Format.SpaceAfter = 6;
        intro.Format.Font.Size = VetReportStyles.SmallSize;
        intro.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);

        var table = section.AddTable();
        table.Borders.Width = 0;
        // The header row repeats on every page. A twelve-page log whose columns are
        // labelled only on page one is unreadable the moment it is put down and picked
        // back up, which is exactly what happens in a waiting room.
        table.Rows.HeightRule = RowHeightRule.AtLeast;

        const double whenWidth = 96;
        const double whatWidth = 120;
        table.AddColumn(Unit.FromPoint(whenWidth));
        table.AddColumn(Unit.FromPoint(whatWidth));
        table.AddColumn(Unit.FromPoint(ctx.ContentWidthPt - whenWidth - whatWidth));

        var head = table.AddRow();
        head.HeadingFormat = true;
        HeaderCell(head, 0, VetReportStrings.ColWhen);
        HeaderCell(head, 1, VetReportStrings.ColWhat);
        HeaderCell(head, 2, VetReportStrings.ColRecorded);

        // Oldest first. The designed report leads with the newest because a vet reads the
        // top of a page and wants the latest; a log is read as a history and runs forward.
        foreach (var line in data.PlainLog.OrderBy(l => l.When))
        {
            var row = table.AddRow();
            BodyCell(row, 0, FormatWhen(line));
            // Verbatim: an owner-defined tracker's name and the reading as it was written
            // down. Never translated, never reformatted, never abbreviated to fit.
            BodyCell(row, 1, line.What);
            BodyCell(row, 2, line.Detail);
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

    /// <summary>Date, plus the time when one was actually recorded. A legacy row without
    /// a time prints the date alone rather than claiming midnight.</summary>
    private static string FormatWhen(ReportLogLine line) =>
        line.HasTime
            ? $"{line.When.ToString(VetReportStyles.DateFormat)}  {line.When:HH:mm}"
            : line.When.ToString(VetReportStyles.DateFormat);
}
