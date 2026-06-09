namespace Animal_Diary_App.Data.ViewModels;

public class ChartDataPoint
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }

    public string DateLabel => Date.ToString("M/d");

    // Pixel height of the bar, normalized against the data range by the
    // view model so the trend is actually visible in the chart.
    public double BarHeight { get; set; }

    // Weight shown above each bar, e.g. "4.2".
    public string ValueLabel => Value.ToString("0.#");
}
