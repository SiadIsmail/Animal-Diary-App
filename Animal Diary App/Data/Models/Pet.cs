namespace Animal_Diary_App.Data.Models;
using SQLite;

public class Pet
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Age { get; set; }
}
public class PetEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public int PetId { get; set; }
    public DateTime Date { get; set; }
    public string Mood { get; set; } = string.Empty;

    public decimal Weight { get; set; }
}

