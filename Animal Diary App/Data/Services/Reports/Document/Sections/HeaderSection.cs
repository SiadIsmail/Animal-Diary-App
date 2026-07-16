namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// Identification block: who the pet is, what it has, what period this covers.
/// Two columns — pet facts left, report metadata right — over a rule line.
/// The optional photo renders only when the DTO carries a path (it never does
/// today; delete the QR-sized Image block below to drop the idea entirely).
/// </summary>
public class HeaderSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => true;

    public void Compose(IContainer container, VetReportData data)
    {
        container
            .BorderBottom(1).BorderColor(VetReportStyles.RuleLine)
            .PaddingBottom(6)
            .Row(row =>
            {
                // Optional passport-style photo, identification only.
                if (data.Pet.PhotoPath != null && File.Exists(data.Pet.PhotoPath))
                    row.ConstantItem(42).PaddingRight(8).Image(data.Pet.PhotoPath).FitArea();

                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(text =>
                    {
                        text.Span(data.Pet.Name).FontSize(VetReportStyles.TitleSize).Bold();
                        text.Span("  " + Signalment(data.Pet)).FontColor(VetReportStyles.InkSecondary);
                    });

                    if (data.Pet.Conditions.Count > 0)
                        col.Item().Text(t =>
                        {
                            t.Span("Conditions: ").SemiBold();
                            t.Span(string.Join(", ", data.Pet.Conditions));
                        });

                    if (data.Pet.CurrentWeightKg is decimal w)
                        col.Item().Text(t =>
                        {
                            t.Span("Weight: ").SemiBold();
                            t.Span($"{w:0.0} kg");
                            if (data.Pet.WeightChangeKg is decimal change)
                                t.Span($"  ({FormatChange(change)} kg over period)").FontColor(VetReportStyles.InkSecondary);
                        });

                    if (data.Pet.OwnerName != null)
                        col.Item().Text(t =>
                        {
                            t.Span("Owner: ").SemiBold();
                            t.Span(data.Pet.OwnerName);
                        });
                });

                row.ConstantItem(150).AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text(
                        $"{data.From.ToString(VetReportStyles.DateFormat)} – {data.To.ToString(VetReportStyles.DateFormat)}").SemiBold();
                    col.Item().AlignRight().Text($"Generated {data.GeneratedAt.ToString(VetReportStyles.DateFormat)}")
                        .FontColor(VetReportStyles.InkSecondary);
                });
            });
    }

    /// <summary>"— Dog, 7 y" plus breed/sex when the app models them one day.</summary>
    private static string Signalment(ReportPetInfo pet)
    {
        var parts = new List<string> { pet.Species };
        if (pet.Breed != null) parts.Add(pet.Breed);
        if (pet.Sex != null) parts.Add(pet.Sex);
        if (pet.AgeYears is int age) parts.Add($"{age} y");
        return "— " + string.Join(", ", parts);
    }

    /// <summary>Explicit sign so gain and loss read unambiguously: "+0.4" / "−1.4".</summary>
    private static string FormatChange(decimal change) =>
        change < 0 ? $"−{Math.Abs(change):0.0}" : $"+{change:0.0}";
}
