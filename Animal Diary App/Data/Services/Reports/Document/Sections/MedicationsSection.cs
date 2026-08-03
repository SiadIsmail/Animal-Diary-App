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
            col.Item().Element(SectionChrome.Title(VetReportStrings.SectionMedications));

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
                    header.Cell().Element(SectionChrome.HeaderCell).Text(VetReportStrings.ColMedication);
                    header.Cell().Element(SectionChrome.HeaderCell).Text(VetReportStrings.ColDose);
                    header.Cell().Element(SectionChrome.HeaderCell).Text(VetReportStrings.ColFrequency);
                    header.Cell().Element(SectionChrome.HeaderCell).Text(VetReportStrings.ColAdherence);
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
            return VetReportStrings.Empty;

        var times = string.Join(", ", med.TimesOfDay.Select(t => t.ToString(VetReportStyles.TimeFormat)));
        return med.DaysPerWeek >= 7
            ? VetReportStrings.FrequencyPerDay(med.TimesOfDay.Count, times)
            : VetReportStrings.FrequencyDaysPerWeek(med.DaysPerWeek, times);
    }

    /// <summary>"given 174 of 180 scheduled doses (2 skipped, 4 missed)". States only
    /// what was recorded; doses with no record yet are simply not counted as given.</summary>
    private static string Adherence(ReportMedication med)
    {
        if (med.ScheduledCount == 0)
            return VetReportStrings.AdherenceUnscheduled(med.TakenCount);

        var text = VetReportStrings.AdherenceGiven(med.TakenCount, med.ScheduledCount);
        var detail = new List<string>();
        if (med.SkippedCount > 0) detail.Add(VetReportStrings.AdherenceSkipped(med.SkippedCount));
        if (med.MissedCount > 0) detail.Add(VetReportStrings.AdherenceMissed(med.MissedCount));
        return detail.Count > 0 ? $"{text} ({string.Join(", ", detail)})" : text;
    }
}
