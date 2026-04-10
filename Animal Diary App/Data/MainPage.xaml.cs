using Animal_Diary_App.Data.Models.Commands;

namespace Animal_Diary_App.Data;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        var addPetCommand = new AddPetCommand();
        var testPet = addPetCommand.Execute("Milo", "Dog", 3);
    }
}
