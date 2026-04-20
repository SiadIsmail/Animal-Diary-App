using Animal_Diary_App.Data.Models;

namespace Animal_Diary_App.Data.Models.Commands;

public class AddPetCommand
{
    public static Pet AddPet(string petName, string petType, int petAge)
    {
        return new Pet
        {
            EnteredPetName = petName,
            EnteredPetType = petType,
            EnteredPetAge = petAge
        };
    }
}

public class AddCalendarEntryCommand
{
    public static PetDiaryEntry AddCalendarEntry(DateTime date, string mood)
    {
        return new PetDiaryEntry
        {
            Date = date,
            Mood = mood
        };
    }
}