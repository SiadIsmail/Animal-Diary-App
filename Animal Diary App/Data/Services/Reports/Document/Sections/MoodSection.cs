namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

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

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        SectionChrome.AddTitle(section, VetReportStrings.SectionMood);

        // States whose reading this is, without qualifying it. The owner's judgement
        // is the data here, so naming it as theirs is accuracy, not a disclaimer.
        var note = section.AddParagraph(VetReportStrings.MoodNote);
        note.Format.Font.Size = VetReportStyles.SmallSize;
        note.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        note.Format.KeepWithNext = true;

        var png = ChartImageRenderer.Observation(
            ctx.ContentWidthPt, VetReportStyles.ChartHeight, data.Mood.Observations, VetReportStrings.MoodRows);
        SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
            label => SectionChrome.CaptionLabel(label, VetReportStrings.MoodChartLabel, "  " + VetReportStrings.Subjective));
    }
}
