namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using MigraDoc.DocumentObjectModel;

/// <summary>
/// One small labelled chart per series in <see cref="VetReportData.Trends"/>, in
/// list order (the builder decides WHICH series exist; this section only draws).
/// Charts are pure facts on axes — no target bands, no annotations, no verdicts.
/// </summary>
public class TrendsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Trends.Count > 0;

    public void Compose(Section section, VetReportData data, ReportContext ctx)
    {
        SectionChrome.AddTitle(section, VetReportStrings.SectionTrends);

        foreach (var series in data.Trends)
        {
            var unitTail = string.IsNullOrEmpty(series.Unit) ? null : $"  ({series.Unit})";

            // Two or more readings → the line chart, its caption welded to it.
            if (series.Points.Count >= 2)
            {
                var png = ChartImageRenderer.Line(ctx.ContentWidthPt, VetReportStyles.ChartHeight, series);
                SectionChrome.AddChartBlock(section, ctx, png, VetReportStyles.ChartHeight,
                    label => SectionChrome.CaptionLabel(label, series.Label, unitTail));
                continue;
            }

            // Otherwise just the caption, plus (for a single reading) the value stated
            // in words — one point on axes would imply a flatness that isn't in the data.
            var caption = section.AddParagraph();
            caption.Format.SpaceBefore = VetReportStyles.ChartSpacing;
            caption.Format.KeepWithNext = true;
            SectionChrome.CaptionLabel(caption, series.Label, unitTail);

            if (series.Points.Count == 1)
            {
                var only = series.Points[0];
                var v = section.AddParagraph();
                v.AddFormattedText($"{only.Value:0.##} ", TextFormat.Bold);
                if (!string.IsNullOrEmpty(series.Unit))
                    v.AddText($"{series.Unit} ");
                var on = v.AddFormattedText(VetReportStrings.OnDate(only.Date.ToString(VetReportStyles.DateFormat)));
                on.Color = SectionChrome.Hex(VetReportStyles.InkSecondary);
            }
        }
    }
}
