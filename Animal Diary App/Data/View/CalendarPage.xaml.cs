namespace Animal_Diary_App.Data.View;

using Syncfusion.Maui.Calendar;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class CalendarPage : ContentPage
{

	public CalendarPage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<CalendarViewModel>() ?? new CalendarViewModel(new PetEntryDatabase());
	}
	async void OnMainClicked(object sender, EventArgs args)
	{
		await Navigation.PushAsync(new MainPage());
	}
	private async void OnMoodEntryCompleted(object? sender, EventArgs e)
	{
		var viewModel = (CalendarViewModel)BindingContext;
		await viewModel.SavePetMoodEntryAsync();
		await viewModel.LoadEntriesAsync();
	}
	private async void OnWeightEntryCompleted(object? sender, EventArgs e)
	{
		var viewModel = (CalendarViewModel)BindingContext;
		await viewModel.SavePetWeightEntryAsync();
		await viewModel.LoadEntriesAsync();
	}
	
	private async void Button_Clicked(object sender, System.EventArgs e)
	{
		this.calendar.IsOpen = true;
	}
	

}