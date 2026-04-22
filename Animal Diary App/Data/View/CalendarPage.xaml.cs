namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class CalendarPage : ContentPage
{

	public CalendarPage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<CalendarViewModel>() ?? new CalendarViewModel();
	}
	async void OnMainClicked(object sender, EventArgs args)
	{
		await Shell.Current.GoToAsync($"/{nameof(MainPage)}", true);
	}
	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		if (BindingContext is CalendarViewModel calendarViewModel)
		{
			calendarViewModel.SaveEntry();
		}
	}


}
