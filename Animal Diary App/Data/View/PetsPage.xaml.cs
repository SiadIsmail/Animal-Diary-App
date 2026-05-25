namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;

public partial class PetsPage : ContentPage
{
	private CalendarPage? calendarPage;

	private MainViewModel vm;
	public PetsPage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	public async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await Navigation.PushAsync(new PetTypePage(vm));
	}

	async void OnMainClicked(object? sender, EventArgs args)
	{
		await Navigation.PopAsync();
	}
	async void OnCalendarClicked(object? sender, EventArgs args)
	{
		calendarPage ??= new CalendarPage(vm);
		await Navigation.PushAsync(calendarPage);
	}

	async void OnAddPetClicked(object? sender, EventArgs args)
	{
		await Navigation.PushAsync(new PetTypePage(vm));
	}

	async void OnAddMedicationClicked(object? sender, EventArgs args)
	{
		await Navigation.PushAsync(new MedicationsPage(vm));
	}
}

