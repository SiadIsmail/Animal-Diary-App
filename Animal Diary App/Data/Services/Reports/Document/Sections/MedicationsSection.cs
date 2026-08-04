namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

/// <summary>
/// The highest-value block for the vet: what is prescribed, how often, and how
/// reliably it was actually given over the period. One table row per medication;
/// all numbers are counted facts (see the DTO), worded without judgement.
/// </summary>
public class MedicationsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Medications.Count > 0;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        SectionChrome.AddTitle(section, VetReportStrings.SectionMedications);

        var table = section.AddTable();
        table.Borders.Width = 0;

        // name 3 · dose 2 · frequency 3 · adherence 4  (of 12)
        var content = ctx.ContentWidthPt;
        foreach (var ratio in new[] { 3d, 2d, 3d, 4d })
            table.AddColumn(Unit.FromPoint(content * ratio / 12d));

        var head = table.AddRow();
        head.HeadingFormat = true;   // repeat the header on page breaks
        HeaderCell(head, 0, VetReportStrings.ColMedication);
        HeaderCell(head, 1, VetReportStrings.ColDose);
        HeaderCell(head, 2, VetReportStrings.ColFrequency);
        HeaderCell(head, 3, VetReportStrings.ColAdherence);

        foreach (var med in data.Medications)
        {
            var row = table.AddRow();
            BodyCellBold(row, 0, med.Name);
            BodyCell(row, 1, $"{med.Dose:0.##} {med.Unit}".Trim());
            BodyCell(row, 2, Frequency(med));
            BodyCell(row, 3, Adherence(med));
        }
    }

    private static void HeaderCell(Row row, int i, string text)
    {
        SectionChrome.ConfigureHeaderCell(row.Cells[i]);
        row.Cells[i].AddParagraph(text);
    }

    private static void BodyCell(Row row, int i, string text)
    {
        SectionChrome.ConfigureBodyCell(row.Cells[i]);
        row.Cells[i].AddParagraph(text);
    }

    private static void BodyCellBold(Row row, int i, string text)
    {
        SectionChrome.ConfigureBodyCell(row.Cells[i]);
        row.Cells[i].AddParagraph().AddFormattedText(text, TextFormat.Bold);
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
