using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.ViewModels;

namespace Animal_Diary_App;

public partial class App : Application
{
	private readonly PetService _petService;
	private readonly MainViewModel _vm;
	private readonly AppDatabase _database;
	private readonly ActivePetService _activePetService;
	private readonly MedicationReminderScheduler _reminderScheduler;

	public App(PetService petService, MainViewModel vm, AppDatabase database, ActivePetService activePetService, MedicationReminderScheduler reminderScheduler)
	{
		InitializeComponent();
		_petService = petService;
		_vm = vm;
		_database = database;
		_activePetService = activePetService;
		_reminderScheduler = reminderScheduler;

		_ = StartAsync();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new LoadingPage());
	}

	private async Task StartAsync()
	{
		try
		{
			await _database.EnsureInitializedAsync();

			var pets = await _petService.GetPetsAsync();

			var savedActivePetId = await _activePetService.GetSavedActivePetIdAsync();
			var activePet = pets.FirstOrDefault(p => p.Id == savedActivePetId) ?? (pets.Count > 0 ? pets[0] : null);

			if (activePet != null)
			{
				await _activePetService.LoadActivePetAsync(activePet.Id);
			}

			var page = pets.Count == 0
				? new NavigationPage(new WelcomePage(_vm))
				: new NavigationPage(new MainPage(_vm));

			if (Application.Current?.Windows.Count > 0)
			{
				Application.Current.Windows[0].Page = page;
			}

			// Re-arm all future reminders on launch. resendMissed:false — the
			// device was on, so the OS already delivered any past reminders;
			// re-sending here would duplicate them. Missed-dose re-sending is the
			// boot receiver's job. Runs off the UI path so startup isn't blocked.
			_ = Task.Run(async () =>
			{
				try
				{
					await _reminderScheduler.CatchUpAndRefreshAsync(resendMissed: false);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine(ex);
				}
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine(ex);
			if (Application.Current?.Windows.Count > 0)
			{
				Application.Current.Windows[0].Page = new ContentPage
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
}
