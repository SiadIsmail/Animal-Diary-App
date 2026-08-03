namespace Animal_Diary_App.Data.Services.Reports.Document;

using Animal_Diary_App.Data.Services.Reports.Document.Sections;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>
/// The whole document = the DTO + an ordered list of sections. To reorder,
/// remove or add a section, edit <see cref="Sections"/> — nothing else. Sections
/// with no content for this pet/range omit themselves via HasContent.
/// </summary>
public class VetReportDocument : IDocument
{
    /// <summary>Document order, top to bottom — decreasing decision-value.</summary>
    private static readonly IVetReportSection[] Sections =
    {
        new HeaderSection(),
        new MedicationsSection(),
        new TrendsSection(),
        new MoodSection(),
        new WaterSection(),
        new AppetiteSection(),
        new EventsSection(),
        new NotesSection(),
    };

    private readonly VetReportData _data;

    public VetReportDocument(VetReportData data) => _data = data;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"{_data.Pet.Name} — Felova health summary",
        Author = "Felova",
        CreationDate = _data.GeneratedAt,
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(VetReportStyles.PageMargin);
            page.DefaultTextStyle(style => style
                .FontFamily(VetReportStyles.FontFamily)
                .FontSize(VetReportStyles.BodySize)
                .FontColor(VetReportStyles.Ink)
                .LineHeight(VetReportStyles.LineHeight));

            // Continuation header — pages 2+ only (SkipOnce), because page 1 already
            // carries the full HeaderSection. Printed vet paperwork gets separated and
            // refiled, so every sheet has to say whose it is and what period it covers;
            // without this a detached page 2 is anonymous.
            page.Header().SkipOnce()
                .PaddingBottom(6)
                .BorderBottom(0.5f).BorderColor(VetReportStyles.RuleLine)
                .Row(row =>
                {
                    row.RelativeItem().Text(VetReportStrings.RunningTitle(_data.Pet.Name))
                        .FontSize(VetReportStyles.SmallSize).SemiBold().FontColor(VetReportStyles.InkSecondary);
                    row.ConstantItem(160).AlignRight().Text(
                            $"{_data.From.ToString(VetReportStyles.DateFormat)} – {_data.To.ToString(VetReportStyles.DateFormat)}")
                        .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                });

            page.Content().Column(col =>
            {
                col.Spacing(VetReportStyles.SectionSpacing);
                foreach (var section in Sections)
                    if (section.HasContent(_data))
                        col.Item().Element(c => section.Compose(c, _data));
            });

            // The footer is the report's one HARD RULE made visible: everything above
            // is owner-reported observation, not a medical record. Never remove it.
            page.Footer()
                .BorderTop(0.5f).BorderColor(VetReportStyles.RuleLine)
                .PaddingTop(3)
                .Row(row =>
                {
                    row.RelativeItem()
                        .Text(VetReportStrings.Footer)
                        .FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary);
                    row.ConstantItem(70).AlignRight().Text(text =>
                    {
                        text.DefaultTextStyle(t => t.FontSize(VetReportStyles.SmallSize).FontColor(VetReportStyles.InkSecondary));
                        text.Span(VetReportStrings.Page + " ");
                        text.CurrentPageNumber();
                        text.Span(" " + VetReportStrings.PageOf + " ");
                        text.TotalPages();
                    });
                });
        });
    }
}
