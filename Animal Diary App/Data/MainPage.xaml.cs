using Animal_Diary_App.Data.Models.Commands;
using Animal_Diary_App.Data;
namespace Animal_Diary_App.Data;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        var testPet = AddPetCommand.AddPet("Milo", "Dog", PetAgePage.PetAge);
        
    }
}
