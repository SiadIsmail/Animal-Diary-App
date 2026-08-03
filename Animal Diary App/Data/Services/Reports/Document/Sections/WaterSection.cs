namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

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
/// The vet interprets; the report only records. Which types appear is the owner's
/// choice in the export sheet (both default on) — the builder already applied it.
/// </summary>
public class WaterSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Water.HasContent;

    public void Compose(IContainer container, VetReportData data)
    {
        var water = data.Water;

        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title(VetReportStrings.SectionWater));
            col.Spacing(VetReportStyles.ChartSpacing);

            // A neutral note that preserves the distinction — descriptive, not a verdict.
            col.Item().Text(VetReportStrings.MeasuredAndObservedNote)
                .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);

            // Objective measurements — quantitative chart. ShowEntire welds each label
            // to its canvas so a page break can't strand the heading (see TrendsSection).
            if (water.Measured is { Points.Count: > 0 } measured)
            {
                col.Item().ShowEntire().Column(chart =>
                {
                    chart.Item().Text(text =>
                    {
                        text.Span(VetReportStrings.Measured).SemiBold().FontSize(VetReportStyles.SmallSize);
                        text.Span($"  ({measured.Unit})")
                            .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                    });
                    chart.Item()
                        .Height(VetReportStyles.ChartHeight)
                        .Canvas((canvas, size) => ChartRenderer.Draw(canvas, size.Width, size.Height, measured));
                });
            }

            // Subjective observations — qualitative chart, kept entirely separate.
            if (water.Observations.Count > 0)
            {
                col.Item().ShowEntire().Column(chart =>
                {
                    chart.Item().Text(text =>
                    {
                        text.Span(VetReportStrings.OwnerObservations).SemiBold().FontSize(VetReportStyles.SmallSize);
                        text.Span("  " + VetReportStrings.Subjective)
                            .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                    });
                    chart.Item()
                        .Height(VetReportStyles.ChartHeight)
                        .Canvas((canvas, size) =>
                            ObservationChartRenderer.Draw(canvas, size.Width, size.Height, water.Observations, VetReportStrings.WaterRows));
                });
            }
        });
    }
}
