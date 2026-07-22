using System.Globalization;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Plugin.LocalNotification;

namespace Animal_Diary_App;

public partial class App : Application
{
	private readonly PetService _petService;
	private readonly MainViewModel _vm;
	private readonly AppDatabase _database;
	private readonly ActivePetService _activePetService;
	private readonly MedicationReminderScheduler _reminderScheduler;
	private readonly SettingsService _settingsService;
	private readonly IAnalyticsService _analytics;
	private readonly ICloudSyncService _cloudSync;
	private readonly IServiceProvider _services;

	public App(PetService petService, MainViewModel vm, AppDatabase database, ActivePetService activePetService, MedicationReminderScheduler reminderScheduler, SettingsService settingsService, IAnalyticsService analytics, ICloudSyncService cloudSync, IServiceProvider services)
	{
		InitializeComponent();
		_petService = petService;
		_vm = vm;
		_database = database;
		_activePetService = activePetService;
		_reminderScheduler = reminderScheduler;
		_settingsService = settingsService;
		_analytics = analytics;
		_cloudSync = cloudSync;
		_services = services;

		// Re-engagement signal: the app was foregrounded by tapping a medication
		// reminder. This is the ONLY place the notification-tap hook is used for
		// analytics; it carries no notification content, just the fact of a tap.
		LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;

		_ = StartAsync();
	}

	private void OnNotificationTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
	{
		// Guard: analytics must never break a user gesture.
		try { _analytics.Track(AnalyticsEvents.NotificationOpened); }
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Analytics] notification_opened failed: {ex.Message}"); }
	}

	/// <summary>
	/// Swap the window root to the tabbed <see cref="AppShell"/>. Called when the
	/// user leaves onboarding (first pet saved). A fresh Shell is resolved so a
	/// post-reset relaunch doesn't reuse stale page instances.
	/// </summary>
	public void SwitchToMainApp()
	{
		if (Windows.Count > 0)
			Windows[0].Page = _services.GetRequiredService<AppShell>();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new LoadingPage());
	}

	protected override void OnResume()
	{
		base.OnResume();
		// Another caregiver/device may have logged while we were backgrounded;
		// debounced so rapid app-switching doesn't hammer the network.
		_cloudSync.RequestSyncSoon();
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

			// Build the post-onboarding landing page lazily so it inflates *after*
			// the chosen language has been applied.
			Page BuildNextPage() => pets.Count == 0
				? new NavigationPage(new WelcomePage(_vm))
				: _services.GetRequiredService<AppShell>();

			var savedLanguage = await _settingsService.GetLanguageAsync();

			Page firstPage;
			if (savedLanguage != null)
			{
				// Returning user: apply their saved language and go straight in.
				LocalizationManager.Instance.SetLanguage(savedLanguage);
				firstPage = BuildNextPage();
			}
			else
			{
				// First launch: seed the UI culture from the device (so the picker
				// reads naturally) then ask the user which language they want.
				var deviceLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
				LocalizationManager.Instance.SetLanguage(deviceLanguage);

				firstPage = new LanguageSelectionPage(_settingsService, code =>
				{
					if (Application.Current?.Windows.Count > 0)
						Application.Current.Windows[0].Page = BuildNextPage();
				});
			}

			if (Application.Current?.Windows.Count > 0)
			{
				Application.Current.Windows[0].Page = firstPage;
			}

			// Analytics: prepare the anonymous id, then record the launch. Fired here so
			// the language property is already applied above and rides along with the
			// event. Wrapped defensively — telemetry must never affect startup.
			try
			{
				await _analytics.InitializeAsync();
				_analytics.Track(AnalyticsEvents.AppOpened, new Dictionary<string, object?>
				{
					[AnalyticsEvents.PropLanguage] = LocalizationManager.Instance.CurrentLanguage,
				});
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Analytics] app_opened failed: {ex.Message}");
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

			// Cloud: load persisted state, then run the launch sync — both off the
			// UI path, both quiet no-ops when signed out / backup disabled / offline.
			_ = Task.Run(async () =>
			{
				try
				{
					await _cloudSync.InitializeAsync();
					await _cloudSync.SyncNowAsync();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[Cloud] launch sync failed: {ex.Message}");
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
						Text = LocalizationManager.Instance.GetString("App_StartError"),
						Margin = 24,
						HorizontalTextAlignment = TextAlignment.Center,
						VerticalTextAlignment = TextAlignment.Center
					}
				};
			}
		}
	}
}
