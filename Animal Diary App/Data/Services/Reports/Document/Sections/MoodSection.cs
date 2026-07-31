namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// How the pet seemed, day by day, as the owner read it. Drawn on the same
/// word-axis chart the water and appetite OBSERVATIONS use, and for the same reason:
/// this is a subjective reading, so it is never plotted as a quantity, never averaged
/// into a score, and never summarised into a direction of travel. The vet interprets;
/// the report records.
///
/// Missing days are simply absent. An unrecorded day is not a zero and not a bad day,
/// and the chart must not let it read as one.
/// </summary>
public class MoodSection : IVetReportSection
{
    // Row labels, level 1 (bottom) → 5 (top), matching MoodLevel. English like every
    // other structural label in the report (see the trend labels).
    private static readonly string[] ObservationRows = { "Unwell", "Low", "Okay", "Good", "Great" };

    public bool HasContent(VetReportData data) => data.Mood.HasContent;

    public void Compose(IContainer container, VetReportData data)
    {
        var mood = data.Mood;

        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Mood"));
            col.Spacing(VetReportStyles.ChartSpacing);

            // States whose reading this is, without qualifying it. The owner's judgement
            // is the data here, so naming it as theirs is accuracy, not a disclaimer.
            col.Item().Text("The owner's own reading of how their pet seemed, as recorded.")
                .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);

            col.Item().Column(chart =>
            {
                chart.Item().Text(text =>
                {
                    text.Span("Daily mood").SemiBold().FontSize(VetReportStyles.SmallSize);
                    text.Span("  (subjective)")
                        .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                });
                chart.Item()
                    .Height(VetReportStyles.ChartHeight)
                    .Canvas((canvas, size) =>
                        ObservationChartRenderer.Draw(canvas, size.Width, size.Height, mood.Observations, ObservationRows));
            });
        });
    }
}
