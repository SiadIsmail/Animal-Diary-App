namespace Animal_Diary_App.Data.View;

using Syncfusion.Maui.Calendar;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class CalendarPage : ContentPage
{
	private MainViewModel vm;
	private PetsPage? petPage;

	public CalendarPage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}


	protected override async void OnAppearing()
	{
		base.OnAppearing();

		if (vm.CalendarVM.Pets.Count == 0)
			await vm.CalendarVM.PrepareDataAsync();
		else
			await vm.CalendarVM.RefreshEntriesAsync();
	}

	async void OnMainClicked(object sender, EventArgs args)
	{
		await Navigation.PopAsync();
	}
	async void OnPetsClicked(object sender, EventArgs args)
	{
		petPage ??= new PetsPage(vm);
		await Navigation.PushAsync(petPage);
	}
	private async void OnMoodEntryCompleted(object? sender, EventArgs e)
	{
		vm.CalendarVM.OnMoodEntryCompleted.Execute(null);
	}


	private async void OnWeightEntryCompleted(object? sender, EventArgs e)
	{
		vm.CalendarVM.OnWeightEntryCompleted.Execute(null);
	}



}