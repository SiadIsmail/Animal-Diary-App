namespace Animal_Diary_App.Data.Services.Reports.Document.Sections;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// One small labelled chart per series in <see cref="VetReportData.Trends"/>, in
/// list order (the builder decides WHICH series exist; this section only draws).
/// Charts are pure facts on axes — no target bands, no annotations, no verdicts.
/// </summary>
public class TrendsSection : IVetReportSection
{
    public bool HasContent(VetReportData data) => data.Trends.Count > 0;

    public void Compose(IContainer container, VetReportData data)
    {
        container.Column(col =>
        {
            col.Item().Element(SectionChrome.Title("Trends"));
            col.Spacing(VetReportStyles.ChartSpacing);

            foreach (var series in data.Trends)
            {
                col.Item().Column(chart =>
                {
                    chart.Item().Text(text =>
                    {
                        text.Span(series.Label).SemiBold().FontSize(VetReportStyles.SmallSize);
                        if (!string.IsNullOrEmpty(series.Unit))
                            text.Span($"  ({series.Unit})")
                                .FontSize(VetReportStyles.SmallSize)
                                .FontColor(VetReportStyles.InkSecondary);
                    });

                    // A single reading can't be a line — one point on axes implies a
                    // flatness that isn't in the data. State it instead, dated, so one
                    // weigh-in still reaches the vet rather than being dropped.
                    if (series.Points.Count == 0)
                        return;

                    if (series.Points.Count == 1)
                    {
                        var only = series.Points[0];
                        chart.Item().Text(text =>
                        {
                            text.Span($"{only.Value:0.##} ").SemiBold();
                            if (!string.IsNullOrEmpty(series.Unit))
                                text.Span($"{series.Unit} ");
                            text.Span($"on {only.Date.ToString(VetReportStyles.DateFormat)}")
                                .FontColor(VetReportStyles.InkSecondary);
                        });
                        return;
                    }

                    chart.Item()
                        .Height(VetReportStyles.ChartHeight)
                        .Canvas((canvas, size) => ChartRenderer.Draw(canvas, size.Width, size.Height, series));
                });
            }
        });
    }
}
