namespace Animal_Diary_App.Data.Models;
using SQLite;

public class MedicationLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public DateTime TakenAt { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string Dosage { get; set; } = string.Empty;
}