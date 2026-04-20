using Animal_Diary_App.Data.Models.Commands;
using Animal_Diary_App.Data;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data.View;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        var petViewModel = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();
        var calendarViewModel = App.Current?.Handler?.MauiContext?.Services.GetService<CalendarViewModel>() ?? new CalendarViewModel();



        PetNameLabel.Text = petViewModel.EnteredPetName;
        PetDetailsLabel.Text = $"{petViewModel.EnteredPetType}, {petViewModel.ParsedPetAge} years old" + Environment.NewLine + $"Your pet is {calendarViewModel.EnteredMood} right now.";

        // This belongs to ViewModel, but for simplicity, it's here for now. We can refactor it later.


    }

    async void OnCalendarClicked(object sender, EventArgs args)
    {
        await Shell.Current.GoToAsync($"/{nameof(CalendarPage)}", true);
    }
}
