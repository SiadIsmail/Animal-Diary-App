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
            PetNameLabel.Text = petViewModel.EnteredPetName;
            PetDetailsLabel.Text = $"{petViewModel.EnteredPetType}, {petViewModel.ParsedPetAge} years old";


        }
        else
        {
            PetDetailsLabel.Text = "Age must be a valid number.";
        }

    }
    
    async void OnCalendarClicked(object sender, EventArgs args)
    {
        await Shell.Current.GoToAsync($"/{nameof(CalendarPage)}", true);
    }
}
