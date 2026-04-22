using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data.View;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<MainPageViewModel>() ?? new MainPageViewModel(new PetViewModel(), new CalendarViewModel());
    }

    async void OnCalendarClicked(object sender, EventArgs args)
    {
        await Shell.Current.GoToAsync($"/{nameof(CalendarPage)}", true);
    }
}
