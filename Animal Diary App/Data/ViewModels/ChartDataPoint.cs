namespace Animal_Diary_App.Data.ViewModels;

public class ChartDataPoint
{
    public DateTime Date { get; set; }
    public decimal Value { get; set; }

    public string DateLabel => Date.ToString("M/d");
}
