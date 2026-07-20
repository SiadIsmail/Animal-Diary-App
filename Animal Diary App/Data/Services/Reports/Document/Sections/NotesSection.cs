namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// The owner's free-text notes from the period, newest first, quoted verbatim.
/// Today these are the journal's mood notes; when a dedicated "questions for the
/// vet" concept exists, give it its own list on the DTO and its own section.
/// </summary>
public class NotesSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Notes.Count > 0;

    public void Compose(IContainer container, VetReportData data)
    {
        var shown = data.Notes.Take(VetReportStyles.MaxNotes).ToList();
        var older = data.Notes.Count - shown.Count;

        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Owner's notes"));
            col.Spacing(2);

            foreach (var note in shown)
                col.Item().Text(text =>
                {
                    text.Span(note.Date.ToString(VetReportStyles.DateFormat) + "  ")
                        .SemiBold().FontColor(VetReportStyles.InkSecondary);
                    text.Span($"“{note.Text}”");
                });

            if (older > 0)
                col.Item().PaddingTop(2).Text($"+ {older} earlier note(s) in the period not listed.")
                    .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
        });
    }
}
