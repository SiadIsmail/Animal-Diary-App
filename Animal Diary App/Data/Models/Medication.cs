namespace Animal_Diary_App.Data.Models;
using SQLite;

public class Medication
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int PetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Dosage { get; set; } = 0;
    public string Notes { get; set; } = string.Empty;
    public bool IsArchived { get; set; } = false;
}