using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.ViewModels;

namespace Animal_Diary_App;

public partial class App : Application
{
	private readonly PetService _petService;
	private readonly MainViewModel _vm;
	private readonly AppDatabase _database;

	public App(PetService petService, MainViewModel vm, AppDatabase database)
	{
		InitializeComponent();
		_petService = petService;
		_vm = vm;
		_database = database;

		MainPage = new LoadingPage();

		_ = StartAsync();
	}

	private async Task StartAsync()
	{
		try
		{
			await _database.EnsureInitializedAsync();

			var pets = await _petService.GetPetsAsync();

			MainPage = pets.Count == 0
				? new NavigationPage(new WelcomePage(_vm))
				: new NavigationPage(new MainPage(_vm));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
			MainPage = new ContentPage
			{
				Content = new Label
				{
					Text = "Could not start the app. Please restart.",
					Margin = 24,
					HorizontalTextAlignment = TextAlignment.Center,
					VerticalTextAlignment = TextAlignment.Center
				}
			};
		}
	}
}
