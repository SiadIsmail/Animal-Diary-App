using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Models;
namespace Animal_Diary_App;

public partial class App : Application
{
	private readonly PetService _petService;
	private readonly MainViewModel _vm;

	public App(PetService petService, MainViewModel vm, AppDatabase database)
	{
		InitializeComponent(); // Refactor this to be have the start up be in a ViewModel -R
		_ = database.InitAsync();
		_petService = petService;
		_vm = vm;

		MainPage = new ContentPage();

		_ = DecideStartPage(_vm, _petService);
	}

	private async Task DecideStartPage(MainViewModel vm, PetService petService)
	{
		var pets = await petService.GetPetsAsync();

		if (pets.Count == 1)
			MainPage = new NavigationPage(new WelcomePage(vm));
		else
			MainPage = new NavigationPage(new MainPage(vm));
	}
}