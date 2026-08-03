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
    public bool HasContent(VetReportData data) => data.Mood.HasContent;

    public void Compose(IContainer container, VetReportData data)
    {
        var mood = data.Mood;

        // One chart, ~130 pt with its heading — small enough to move to the next page
        // whole rather than be split across one. ShowEntire on the section itself keeps
        // the title, the note and the chart together.
        container.ShowEntire().Column(col =>
        {
            col.Item().Element(SectionChrome.Title(VetReportStrings.SectionMood));
            col.Spacing(VetReportStyles.ChartSpacing);

            // States whose reading this is, without qualifying it. The owner's judgement
            // is the data here, so naming it as theirs is accuracy, not a disclaimer.
            col.Item().Text(VetReportStrings.MoodNote)
                .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);

            col.Item().Column(chart =>
            {
                chart.Item().Text(text =>
                {
                    text.Span(VetReportStrings.MoodChartLabel).SemiBold().FontSize(VetReportStyles.SmallSize);
                    text.Span("  " + VetReportStrings.Subjective)
                        .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                });
                chart.Item()
                    .Height(VetReportStyles.ChartHeight)
                    .Canvas((canvas, size) =>
                        ObservationChartRenderer.Draw(canvas, size.Width, size.Height, mood.Observations, VetReportStrings.MoodRows));
            });
        });
    }
}
