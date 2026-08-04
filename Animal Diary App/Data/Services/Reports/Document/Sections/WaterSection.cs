namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// Water intake — the report's clearest statement of its own philosophy: Felova is a
/// communication layer, not a medical-interpretation one. Water holds two DISTINCT
/// data types, and this section keeps them apart:
/// <list type="bullet">
/// <item><b>Measured</b> — objective millilitres, drawn as a quantitative chart
///   (<see cref="ChartRenderer"/>, the same line chart as weight/glucose).</item>
/// <item><b>Owner observations</b> — subjective readings, drawn on their OWN chart
///   with a word axis (<see cref="ObservationChartRenderer"/>).</item>
/// </list>
/// The two graphs share this section but are NEVER combined into one visualization,
/// observations are NEVER converted to numbers, and no trend or verdict is stated.
/// </summary>
public class WaterSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Water.HasContent;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var water = data.Water;

        SectionChrome.AddTitle(section, VetReportStrings.SectionWater);

        // A neutral note that preserves the distinction — descriptive, not a verdict.
        var note = section.AddParagraph(VetReportStrings.MeasuredAndObservedNote);
        note.Format.Font.Size = VetReportStyles.SmallSize;
        note.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        note.Format.KeepWithNext = true;

        // Objective measurements — quantitative chart.
        if (water.Measured is { Points.Count: > 0 } measured)
        {
            var png = ChartImageRenderer.Line(ctx.ContentWidthPt, VetReportStyles.ChartHeight, measured);
            SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
                label => SectionChrome.CaptionLabel(label, VetReportStrings.Measured, $"  ({measured.Unit})"));
        }

        // Subjective observations — qualitative chart, kept entirely separate.
        if (water.Observations.Count > 0)
        {
            var png = ChartImageRenderer.Observation(
                ctx.ContentWidthPt, VetReportStyles.ChartHeight, water.Observations, VetReportStrings.WaterRows);
            SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
                label => SectionChrome.CaptionLabel(label, VetReportStrings.OwnerObservations, "  " + VetReportStrings.Subjective));
        }
    }
}
