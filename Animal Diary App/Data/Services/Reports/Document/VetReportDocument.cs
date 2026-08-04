namespace Animal_Diary_App.Data.Services.Reports.Document;

using Animal_Diary_App.Data.Services.Reports.Document.Sections;
using MigraDoc.DocumentObjectModel;

/// <summary>
/// Builds the whole report as a MigraDoc <see cref="MigraDoc.DocumentObjectModel.Document"/>:
/// the DTO plus an ordered list of sections. To reorder, remove or add a section, edit
/// <see cref="Sections"/> — nothing else. Sections with no content for this pet/range omit
/// themselves via <see cref="IVetReportSection.HasContent"/>.
///
/// The caller owns the <see cref="ReportContext"/> (its temp chart files must survive until
/// the PDF is rendered) — see <c>VetReportService</c>.
/// </summary>
public sealed class VetReportDocument
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

    // A4 is 21.0 cm wide (72 pt/inch, 2.54 cm/inch → 595.276 pt); usable width is that
    // minus the two side margins. Sections size charts and columns against this.
    private const double A4WidthPt = 595.276;
    public const double ContentWidthPt = A4WidthPt - 2 * VetReportStyles.PageMargin;

    private readonly VetReportData _data;

    public VetReportDocument(VetReportData data) => _data = data;

    public Document Build(ReportContext ctx)
    {
        var doc = new Document();
        doc.Info.Title = $"{_data.Pet.Name} — Felova health summary";
        doc.Info.Author = "Felova";

        var normal = doc.Styles["Normal"]!;
        normal.Font.Name = ReportFontResolver.FamilyName;
        normal.Font.Size = VetReportStyles.BodySize;
        normal.Font.Color = SectionChrome.Hex(VetReportStyles.Ink);

        var section = doc.AddSection();
        var page = section.PageSetup;
        page.PageFormat = PageFormat.A4;
        var margin = Unit.FromPoint(VetReportStyles.PageMargin);
        page.TopMargin = page.BottomMargin = page.LeftMargin = page.RightMargin = margin;
        // Page 1 carries the full HeaderSection, so its running header is suppressed; the
        // footer (disclaimer + page numbers) appears on every page.
        page.DifferentFirstPageHeaderFooter = true;

        BuildContinuationHeader(section);
        BuildFooter(section.Footers.Primary);
        BuildFooter(section.Footers.FirstPage);

        foreach (var s in Sections)
            if (s.HasContent(_data))
                s.Compose(section, _data, ctx);

        return doc;
    }

    // Pages 2+ only. Printed vet paperwork gets separated and refiled, so every sheet has
    // to say whose it is and what period it covers; without this a detached page 2 is
    // anonymous. (Page 1's FirstPage header is deliberately left empty.)
    private void BuildContinuationHeader(Section section)
    {
        var p = section.Headers.Primary.AddParagraph();
        p.Format.Font.Size = VetReportStyles.SmallSize;
        p.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        p.Format.SpaceAfter = 6;
        p.Format.Borders.Bottom.Width = 0.5;
        p.Format.Borders.Bottom.Color = SectionChrome.Hex(VetReportStyles.RuleLine);
        p.Format.AddTabStop(Unit.FromPoint(ContentWidthPt), TabAlignment.Right);

        var title = p.AddFormattedText(VetReportStrings.RunningTitle(_data.Pet.Name), TextFormat.Bold);
        title.Size = VetReportStyles.SmallSize;
        p.AddTab();
        p.AddText($"{_data.From.ToString(VetReportStyles.DateFormat)} – {_data.To.ToString(VetReportStyles.DateFormat)}");
    }

    // The footer is the report's one HARD RULE made visible: everything above is
    // owner-reported observation, not a medical record. Never remove it.
    private void BuildFooter(HeaderFooter footer)
    {
        var p = footer.AddParagraph();
        p.Format.Font.Size = VetReportStyles.SmallSize;
        p.Format.Font.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
        p.Format.SpaceBefore = 3;
        p.Format.Borders.Top.Width = 0.5;
        p.Format.Borders.Top.Color = SectionChrome.Hex(VetReportStyles.RuleLine);
        p.Format.AddTabStop(Unit.FromPoint(ContentWidthPt), TabAlignment.Right);

        p.AddText(VetReportStrings.Footer);
        p.AddTab();
        p.AddText(VetReportStrings.Page + " ");
        p.AddPageField();
        p.AddText(" " + VetReportStrings.PageOf + " ");
        p.AddNumPagesField();
    }
}
