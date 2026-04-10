using Animal_Diary_App.Data.Models;

namespace Animal_Diary_App.Data.Models.Commands;

public class AddPetCommand
{
    public Pet Execute(string petName, string petType, int petAge)
    {
        return new Pet
        {
            EnteredPetName = petName,
            EnteredPetType = petType,
            EnteredPetAge = petAge
        };
    }
}