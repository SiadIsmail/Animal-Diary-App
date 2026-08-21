namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// Appetite: the same communication-not-interpretation stance as
/// <see cref="WaterSection"/>, with a diet list added. Three distinct things, kept
/// apart and never interpreted:
/// <list type="bullet">
/// <item><b>Measured</b>: objective grams, a quantitative chart
///   (<see cref="ChartRenderer"/>).</item>
/// <item><b>Owner observations</b>: the qualitative reading, on its OWN word-axis
///   chart (<see cref="ObservationChartRenderer"/>).</item>
/// <item><b>Foods recorded</b>: a plain list of the foods logged in the range (the
///   diet); not food-change tracking, no interpretation.</item>
/// </list>
/// </summary>
public class AppetiteSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Appetite.HasContent;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var appetite = data.Appetite;

        SectionChrome.AddTitle(section, VetReportStrings.SectionAppetite);

        var note = section.AddParagraph(VetReportStrings.MeasuredAndObservedNote);
        note.Format.Font.Size = VetReportStyles.SmallSize;
        note.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        note.Format.KeepWithNext = true;

        // Objective measurements: quantitative chart.
        if (appetite.Measured is { Points.Count: > 0 } measured)
        {
            var png = ChartImageRenderer.Line(ctx.ContentWidthPt, VetReportStyles.ChartHeight, measured);
            SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
                label => SectionChrome.CaptionLabel(label, VetReportStrings.Measured, $"  ({measured.Unit})"));
        }

        // Subjective observations: qualitative chart, kept entirely separate.
        if (appetite.Observations.Count > 0)
        {
            var png = ChartImageRenderer.Observation(
                ctx.ContentWidthPt, VetReportStyles.ChartHeight, appetite.Observations, VetReportStrings.AppetiteRows);
            SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
                label => SectionChrome.CaptionLabel(label, VetReportStrings.OwnerObservations, "  " + VetReportStrings.Subjective));
        }

        // Diet: the foods recorded in the range, as a plain list. No frequencies,
        // no "changed to", no interpretation; the range is the context.
        if (appetite.Foods.Count > 0)
        {
            var label = section.AddParagraph();
            label.Format.SpaceBefore = VetReportStyles.ChartSpacing;
            label.Format.KeepWithNext = true;
            var lead = label.AddFormattedText(VetReportStrings.FoodsRecorded, TextFormat.Bold);
            lead.Size = VetReportStyles.SmallSize;

            var list = section.AddParagraph(string.Join(" · ", appetite.Foods));
            list.Format.Font.Size = VetReportStyles.SmallSize;
            list.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        }
    }
}
