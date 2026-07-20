namespace Animal_Diary_App.Data.Services.Reports.Document;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// The few visual elements every section shares (title style, table cells), so
/// they look identical without sections knowing about each other. Layout numbers
/// still come from <see cref="VetReportStyles"/>.
/// </summary>
public static class SectionChrome
{
    /// <summary>Uppercase section heading with a little air underneath.</summary>
    public static Action<IContainer> Title(string text) => container =>
        container.PaddingBottom(3).Text(text.ToUpperInvariant())
            .FontSize(VetReportStyles.SectionTitleSize).Bold();

    /// <summary>Table header cell: bold-ish label over a solid line.</summary>
    public static IContainer HeaderCell(IContainer container) => container
        .BorderBottom(0.8f).BorderColor(VetReportStyles.Ink)
        .PaddingHorizontal(VetReportStyles.CellPaddingX)
        .PaddingVertical(VetReportStyles.CellPaddingY)
        .DefaultTextStyle(t => t.SemiBold().FontSize(VetReportStyles.SmallSize));

    /// <summary>Table body cell with a hairline row separator.</summary>
    public static IContainer BodyCell(IContainer container) => container
        .BorderBottom(0.4f).BorderColor(VetReportStyles.TableLine)
        .PaddingHorizontal(VetReportStyles.CellPaddingX)
        .PaddingVertical(VetReportStyles.CellPaddingY);
}
