namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// The owner's free-text notes from the period, newest first, quoted verbatim.
/// Today these are the journal's mood notes; when a dedicated "questions for the
/// vet" concept exists, give it its own list on the DTO and its own section.
/// </summary>
public class NotesSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Notes.Count > 0;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var shown = data.Notes.Take(VetReportStyles.MaxNotes).ToList();
        var older = data.Notes.Count - shown.Count;

        SectionChrome.AddTitle(section, VetReportStrings.SectionNotes);

        foreach (var note in shown)
        {
            var p = section.AddParagraph();
            p.Format.SpaceAfter = 2;
            var date = p.AddFormattedText(note.Date.ToString(VetReportStyles.DateFormat) + "  ", TextFormat.Bold);
            date.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
            // The owner's own words, printed verbatim — never translated.
            p.AddText($"“{note.Text}”");
        }

        if (older > 0)
        {
            var more = section.AddParagraph(VetReportStrings.MoreNotes(older));
            more.Format.SpaceBefore = 2;
            more.Format.Font.Size = VetReportStyles.SmallSize;
            more.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        }
    }
}
