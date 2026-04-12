using Animal_Diary_App.Data.Models.Commands;
using Animal_Diary_App.Data;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();

        if (BindingContext is PetViewModel petViewModel && petViewModel.ParsedPetAge.HasValue)
        {
            var testPet = AddPetCommand.AddPet(
                petViewModel.EnteredPetName,
                petViewModel.EnteredPetType,
                petViewModel.ParsedPetAge.Value);

            // Show final values to prove all pages contributed to one shared model.
            PetNameLabel.Text = testPet.EnteredPetName;
            PetDetailsLabel.Text = $"{testPet.EnteredPetType}, {testPet.EnteredPetAge} years old";
        }
        else
        {
            PetDetailsLabel.Text = "Age must be a valid number.";
        }
    }
}
