namespace Animal_Diary_App.Data;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class CalendarPage : ContentPage
{

	public CalendarPage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();
	}
    async void OnMainClicked(object sender, EventArgs args)
    {
        await Shell.Current.GoToAsync($"///{nameof(MainPage)}");
    }
    
	
	
}
