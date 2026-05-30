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

public class MedicationSchedule
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int MedicationId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeSpan Time { get; set; }


}

public class MedicationTime
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int MedicationId { get; set; }
}

public class FilteredMedication
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string DoseDisplay { get; set; } = string.Empty;
    public string FrequencyDisplay { get; set; } = string.Empty;
    public TimeSpan TimesDisplay { get; set; }
    public string Note { get; set; } = string.Empty;
}
