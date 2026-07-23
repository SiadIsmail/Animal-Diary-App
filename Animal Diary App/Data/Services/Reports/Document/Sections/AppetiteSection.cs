namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// Appetite — the same communication-not-interpretation stance as
/// <see cref="WaterSection"/>, with a diet list added. Three distinct things, kept
/// apart and never interpreted:
/// <list type="bullet">
/// <item><b>Measured</b> — objective grams, a quantitative chart
///   (<see cref="ChartRenderer"/>).</item>
/// <item><b>Owner observations</b> — the qualitative reading, on its OWN word-axis
///   chart (<see cref="ObservationChartRenderer"/>).</item>
/// <item><b>Foods recorded</b> — a plain list of the foods logged in the range (the
///   diet); not food-change tracking, no interpretation.</item>
/// </list>
/// The two graphs are never combined, observations are never numbered, and no trend
/// or verdict is stated. Which parts appear is the owner's export choice (builder
/// already applied it).
/// </summary>
public class AppetiteSection : IVetReportSection
{
    // The observation chart's category rows, level 1 (bottom) → 5 (top). English, like
    // every other structural label in the report.
    private static readonly string[] ObservationRows = { "Nothing", "A little", "About half", "Most of it", "Everything" };

    public bool HasContent(VetReportData data) => data.Appetite.HasContent;

    public void Compose(IContainer container, VetReportData data)
    {
        var appetite = data.Appetite;

        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Appetite"));
            col.Spacing(VetReportStyles.ChartSpacing);

            col.Item().Text(
                    "Measured amounts and the owner's own observations are shown separately, as recorded.")
                .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);

            // Objective measurements — quantitative chart.
            if (appetite.Measured is { Points.Count: > 0 } measured)
            {
                col.Item().Column(chart =>
                {
                    chart.Item().Text(text =>
                    {
                        text.Span("Measured").SemiBold().FontSize(VetReportStyles.SmallSize);
                        text.Span($"  ({measured.Unit})")
                            .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                    });
                    chart.Item()
                        .Height(VetReportStyles.ChartHeight)
                        .Canvas((canvas, size) => ChartRenderer.Draw(canvas, size.Width, size.Height, measured));
                });
            }

            // Subjective observations — qualitative chart, kept entirely separate.
            if (appetite.Observations.Count > 0)
            {
                col.Item().Column(chart =>
                {
                    chart.Item().Text(text =>
                    {
                        text.Span("Owner observations").SemiBold().FontSize(VetReportStyles.SmallSize);
                        text.Span("  (subjective)")
                            .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                    });
                    chart.Item()
                        .Height(VetReportStyles.ChartHeight)
                        .Canvas((canvas, size) =>
                            ObservationChartRenderer.Draw(canvas, size.Width, size.Height, appetite.Observations, ObservationRows));
                });
            }

            // Diet — the foods recorded in the range, as a plain list. No frequencies,
            // no "changed to", no interpretation; the range is the context.
            if (appetite.Foods.Count > 0)
            {
                col.Item().Column(diet =>
                {
                    diet.Item().Text("Foods recorded").SemiBold().FontSize(VetReportStyles.SmallSize);
                    diet.Item().Text(string.Join(" · ", appetite.Foods))
                        .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                });
            }
        });
    }
}
