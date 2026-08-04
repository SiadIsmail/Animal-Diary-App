namespace Animal_Diary_App.Data.Services.Reports.Document;

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;

/// <summary>
/// The few visual elements every section shares (colours, the section title, table
/// cells, and a chart block), so they look identical without sections knowing about
/// each other. Layout numbers still come from <see cref="VetReportStyles"/>.
/// </summary>
public static class SectionChrome
{
    /// <summary>Parse a "#rrggbb" style token into a MigraDoc colour.</summary>
    public static Color Hex(string hex)
    {
        var h = hex.TrimStart('#');
        var r = Convert.ToByte(h.Substring(0, 2), 16);
        var g = Convert.ToByte(h.Substring(2, 2), 16);
        var b = Convert.ToByte(h.Substring(4, 2), 16);
        return new Color(r, g, b);
    }

    /// <summary>Uppercase section heading with air above (between sections) and below.</summary>
    public static Paragraph AddTitle(Section section, string text)
    {
        var p = section.AddParagraph(text.ToUpperInvariant());
        p.Format.Font.Bold = true;
        p.Format.Font.Size = VetReportStyles.SectionTitleSize;
        p.Format.SpaceBefore = VetReportStyles.SectionSpacing;
        p.Format.SpaceAfter = 3;
        p.Format.KeepWithNext = true;
        return p;
    }

    /// <summary>Table header cell: semibold label over a solid line.</summary>
    public static void ConfigureHeaderCell(Cell cell)
    {
        cell.Borders.Bottom.Width = 0.8;
        cell.Borders.Bottom.Color = Hex(VetReportStyles.Ink);
        cell.Format.Font.Bold = true;
        cell.Format.Font.Size = VetReportStyles.SmallSize;
        Pad(cell);
    }

    /// <summary>Table body cell with a hairline row separator.</summary>
    public static void ConfigureBodyCell(Cell cell)
    {
        cell.Borders.Bottom.Width = 0.4;
        cell.Borders.Bottom.Color = Hex(VetReportStyles.TableLine);
        Pad(cell);
    }

    private static void Pad(Cell cell)
    {
        // MigraDoc has no cell padding; approximate QuestPDF's with paragraph spacing
        // (vertical) and indents (horizontal).
        cell.Format.SpaceBefore = VetReportStyles.CellPaddingY;
        cell.Format.SpaceAfter = VetReportStyles.CellPaddingY;
        cell.Format.LeftIndent = VetReportStyles.CellPaddingX;
        cell.Format.RightIndent = VetReportStyles.CellPaddingX;
        cell.VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>A labelled chart: a caption paragraph welded to the chart image below it,
    /// so a page break can never strand the heading (QuestPDF's ShowEntire equivalent).</summary>
    public static void AddChartBlock(
        Section section, ReportContext ctx, byte[] png, double heightPt, Action<Paragraph> buildLabel)
    {
        var label = section.AddParagraph();
        label.Format.SpaceBefore = VetReportStyles.ChartSpacing;
        label.Format.KeepWithNext = true;
        buildLabel(label);

        var holder = section.AddParagraph();
        holder.Format.KeepTogether = true;
        var img = holder.AddImage(ctx.TempImage(png));
        img.LockAspectRatio = false;
        img.Width = Unit.FromPoint(ctx.ContentWidthPt);
        img.Height = Unit.FromPoint(heightPt);
    }

    /// <summary>Small caption text ("Measured (mL)", etc.): bold lead + optional grey tail.</summary>
    public static void CaptionLabel(Paragraph p, string lead, string? tail = null)
    {
        var l = p.AddFormattedText(lead, TextFormat.Bold);
        l.Size = VetReportStyles.SmallSize;
        if (!string.IsNullOrEmpty(tail))
        {
            var t = p.AddFormattedText(tail);
            t.Size = VetReportStyles.SmallSize;
            t.Color = Hex(VetReportStyles.InkSecondary);
        }
    }
}
