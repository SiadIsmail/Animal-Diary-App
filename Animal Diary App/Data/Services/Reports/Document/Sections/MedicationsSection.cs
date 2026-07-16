namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// The highest-value block for the vet: what is prescribed, how often, and how
/// reliably it was actually given over the period. One table row per medication;
/// all numbers are counted facts (see the DTO), worded without judgement.
/// </summary>
public class MedicationsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Medications.Count > 0;

    public void Compose(IContainer container, VetReportData data)
    {
        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Medications"));

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);   // name
                    columns.RelativeColumn(2);   // dose
                    columns.RelativeColumn(3);   // frequency
                    columns.RelativeColumn(4);   // adherence
                });

                table.Header(header =>
                {
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Medication");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Dose");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Frequency");
                    header.Cell().Element(SectionChrome.HeaderCell).Text("Adherence (this period)");
                });

                foreach (var med in data.Medications)
                {
                    table.Cell().Element(SectionChrome.BodyCell).Text(med.Name).SemiBold();
                    table.Cell().Element(SectionChrome.BodyCell).Text($"{med.Dose:0.##} {med.Unit}".Trim());
                    table.Cell().Element(SectionChrome.BodyCell).Text(Frequency(med));
                    table.Cell().Element(SectionChrome.BodyCell).Text(Adherence(med));
                }
            });
        });
    }

    /// <summary>"2×/day (08:00, 20:00)" for everyday meds, "3 days/week, 08:00" otherwise.</summary>
    private static string Frequency(ReportMedication med)
    {
        if (med.TimesOfDay.Count == 0)
            return "—";

        var times = string.Join(", ", med.TimesOfDay.Select(t => t.ToString(VetReportStyles.TimeFormat)));
        return med.DaysPerWeek >= 7
            ? $"{med.TimesOfDay.Count}×/day ({times})"
            : $"{med.DaysPerWeek} days/week, {times}";
    }

    /// <summary>"given 174 of 180 scheduled doses (2 skipped, 4 missed)". States only
    /// what was recorded; doses with no record yet are simply not counted as given.</summary>
    private static string Adherence(ReportMedication med)
    {
        if (med.ScheduledCount == 0)
            return $"given {med.TakenCount} doses (unscheduled)";

        var text = $"given {med.TakenCount} of {med.ScheduledCount} scheduled doses";
        var detail = new List<string>();
        if (med.SkippedCount > 0) detail.Add($"{med.SkippedCount} skipped");
        if (med.MissedCount > 0) detail.Add($"{med.MissedCount} missed");
        return detail.Count > 0 ? $"{text} ({string.Join(", ", detail)})" : text;
    }
}
