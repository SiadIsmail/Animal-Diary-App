using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.Models;
namespace Animal_Diary_App;

public partial class App : Application
{
	private readonly PetDatabase _database;
	public App(PetDatabase database)
	{
		InitializeComponent();
		_database = database;

		MainPage = new ContentPage(); 

		DecideStartPage();


	}

	public async void DecideStartPage()
	{
		var pets = await _database.GetPetsAsync();
		if (pets.Count == 0)
		{
			MainPage = new NavigationPage(new WelcomePage());
		}
		else
		{
			MainPage = new NavigationPage(new MainPage());
		}
	}
}