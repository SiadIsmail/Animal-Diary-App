namespace Animal_Diary_App.Data.Models;

public class Pet
{
    public string EnteredPetName { get; set; } = string.Empty;
    public string EnteredPetType { get; set; } = string.Empty;
    public int EnteredPetAge { get; set; }
}
public class PetDiaryEntry
{
    public DateTime Date { get; set; }
    public string Mood { get; set; } = string.Empty;
}

