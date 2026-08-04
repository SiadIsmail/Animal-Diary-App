namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// Identification block: who the pet is, what it has, what period this covers.
/// Two columns — pet facts left, report metadata right — over a rule line.
/// The optional photo renders only when the DTO carries a path (it never does today).
/// </summary>
public class HeaderSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => true;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        var pet = data.Pet;
        var hasPhoto = pet.PhotoPath != null && File.Exists(pet.PhotoPath);

        var table = section.AddTable();
        table.Borders.Width = 0;

        const double photoWidth = 50;
        const double metaWidth = 150;
        var leftWidth = ctx.ContentWidthPt - metaWidth - (hasPhoto ? photoWidth : 0);

        if (hasPhoto) table.AddColumn(Unit.FromPoint(photoWidth));
        table.AddColumn(Unit.FromPoint(leftWidth));
        table.AddColumn(Unit.FromPoint(metaWidth));

        var row = table.AddRow();
        row.Borders.Bottom.Width = 1;
        row.Borders.Bottom.Color = SectionChrome.Hex(VetReportStyles.RuleLine);

        var col = 0;
        if (hasPhoto)
        {
            var img = row.Cells[col++].AddParagraph().AddImage(pet.PhotoPath!);
            img.LockAspectRatio = true;
            img.Width = Unit.FromPoint(42);
        }

        // ── Left: pet facts ────────────────────────────────────────────────
        var left = row.Cells[col++];
        left.Format.SpaceAfter = 6;

        var namePara = left.AddParagraph();
        var name = namePara.AddFormattedText(pet.Name, TextFormat.Bold);
        name.Size = VetReportStyles.TitleSize;
        var sig = namePara.AddFormattedText("  " + Signalment(pet));
        sig.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);

        if (pet.Conditions.Count > 0)
        {
            var p = left.AddParagraph();
            p.AddFormattedText(VetReportStrings.Conditions + " ", TextFormat.Bold);
            p.AddText(string.Join(", ", pet.Conditions));
        }

        if (pet.CurrentWeightKg is decimal w)
        {
            var p = left.AddParagraph();
            p.AddFormattedText(VetReportStrings.Weight + " ", TextFormat.Bold);
            p.AddText($"{w:0.0} kg");
            if (pet.WeightChangeKg is decimal change)
            {
                var c = p.AddFormattedText("  " + VetReportStrings.WeightChange(FormatChange(change)));
                c.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
            }
        }

        if (pet.OwnerName != null)
        {
            var p = left.AddParagraph();
            p.AddFormattedText(VetReportStrings.Owner + " ", TextFormat.Bold);
            p.AddText(pet.OwnerName);
        }

        // ── Right: report metadata ─────────────────────────────────────────
        var meta = row.Cells[col];
        meta.Format.Alignment = ParagraphAlignment.Right;
        meta.Format.SpaceAfter = 6;

        meta.AddParagraph().AddFormattedText(
            $"{data.From.ToString(VetReportStyles.DateFormat)} – {data.To.ToString(VetReportStyles.DateFormat)}",
            TextFormat.Bold);
        var gen = meta.AddParagraph().AddFormattedText(
            VetReportStrings.Generated(data.GeneratedAt.ToString(VetReportStyles.DateFormat)));
        gen.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
    }

    /// <summary>"— Dog, 7 y" plus breed/sex when the app models them one day. Species
    /// arrives already localized from the builder (PetTypeNames.Localize).</summary>
    private static string Signalment(ReportPetInfo pet)
    {
        var parts = new List<string> { pet.Species };
        if (pet.Breed != null) parts.Add(pet.Breed);
        if (pet.Sex != null) parts.Add(pet.Sex);
        if (pet.AgeYears is int age) parts.Add(VetReportStrings.AgeYears(age));
        return "— " + string.Join(", ", parts);
    }

    /// <summary>Explicit sign so gain and loss read unambiguously: "+0.4" / "−1.4".</summary>
    private static string FormatChange(decimal change) =>
        change < 0 ? $"−{Math.Abs(change):0.0}" : $"+{change:0.0}";
}
