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
	private readonly DailyCareReminderScheduler _dailyReminderScheduler;
	private readonly Animal_Diary_App.Data.Services.Data.Device.INotificationService _notifications;
	private readonly SettingsService _settingsService;
	private readonly IAnalyticsService _analytics;
	private readonly ICloudSyncService _cloudSync;
	private readonly Animal_Diary_App.Data.Services.Billing.IEntitlementService _entitlements;
	private readonly MedicationDoseLogService _doseLogs;
	private readonly IServiceProvider _services;

	public App(PetService petService, MainViewModel vm, AppDatabase database, ActivePetService activePetService, MedicationReminderScheduler reminderScheduler, DailyCareReminderScheduler dailyReminderScheduler, Animal_Diary_App.Data.Services.Data.Device.INotificationService notifications, SettingsService settingsService, IAnalyticsService analytics, ICloudSyncService cloudSync, Animal_Diary_App.Data.Services.Billing.IEntitlementService entitlements, MedicationDoseLogService doseLogs, IServiceProvider services)
	{
		InitializeComponent();
		_petService = petService;
		_vm = vm;
		_database = database;
		_activePetService = activePetService;
		_reminderScheduler = reminderScheduler;
		_dailyReminderScheduler = dailyReminderScheduler;
		_notifications = notifications;
		_settingsService = settingsService;
		_analytics = analytics;
		_cloudSync = cloudSync;
		_entitlements = entitlements;
		_doseLogs = doseLogs;
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
		// resuming re-enables the foreground poll and schedules a debounced sync.
		_cloudSync.NotifyAppState(foreground: true);

		// Re-evaluate today's daily care reminder: the day may have rolled over, or
		// items were logged in another session, so it may now need arming or cancelling.
		_ = _dailyReminderScheduler.RefreshAsync();

		// A subscription may have been bought/renewed/cancelled elsewhere while we were
		// backgrounded; re-check the entitlement. No-op under the Null boundary. Then, if
		// the trial has quietly elapsed while away, show the one-time reassurance.
		_ = Task.Run(async () =>
		{
			await _entitlements.RefreshAsync();
			await MaybeShowReadOnlyReassuranceAsync();
			await MaybeShowPreEndNudgeAsync();
		});
	}

	/// <summary>Once, a few days before the trial ends, a gentle heads-up anchored to what
	/// the owner has actually built (real dose count + weeks tracked) — loss aversion, not a
	/// countdown drumbeat. Mutually exclusive with the read-only reassurance (that's
	/// TrialExpired, this is Trial). No-op under the Null boundary.</summary>
	private async Task MaybeShowPreEndNudgeAsync()
	{
		try
		{
			if (_entitlements.State != Animal_Diary_App.Data.Services.Billing.AccessState.Trial)
				return;
			if (_entitlements.TrialDaysLeft > Animal_Diary_App.Data.Services.Billing.BillingConfig.PreEndNudgeDaysBefore)
				return;
			if (await _settingsService.GetFlagAsync(SettingsFlags.PreEndNudgeShown))
				return;
			await _settingsService.SetFlagAsync(SettingsFlags.PreEndNudgeShown, true);

			var pet = _vm.PetVM.ActivePet;
			var petName = pet?.Name ?? string.Empty;
			var doseCount = pet != null ? await _doseLogs.GetGivenCountAsync(pet.Id) : 0;
			var startUtc = await _settingsService.GetTrialStartUtcAsync();
			var weeks = startUtc is DateTime s
				? Math.Max(1, (int)Math.Ceiling((DateTime.UtcNow - s).TotalDays / 7))
				: 1;
			var daysLeft = _entitlements.TrialDaysLeft;

			MainThread.BeginInvokeOnMainThread(() => _vm.TrialMessageVM.ShowNudge(petName, daysLeft, doseCount, weeks));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Billing] pre-end nudge failed: {ex.Message}");
		}
	}

	/// <summary>Once, when the app first finds itself in the care-only read state (trial
	/// elapsed, no subscription), reassure the owner their data is safe. Reassures first;
	/// the continue-to-subscribe ask lives inside that sheet. No-op under the Null boundary
	/// (state is always Subscribed there).</summary>
	private async Task MaybeShowReadOnlyReassuranceAsync()
	{
		try
		{
			if (_entitlements.State != Animal_Diary_App.Data.Services.Billing.AccessState.TrialExpired)
				return;
			if (await _settingsService.GetFlagAsync(SettingsFlags.ReadOnlyReassuranceShown))
				return;
			await _settingsService.SetFlagAsync(SettingsFlags.ReadOnlyReassuranceShown, true);

			var petName = _vm.PetVM.ActivePet?.Name ?? string.Empty;
			var trialDay = (int)Animal_Diary_App.Data.Services.Billing.BillingConfig.TrialLength.TotalDays;
			MainThread.BeginInvokeOnMainThread(() => _vm.TrialMessageVM.ShowReadOnly(petName, trialDay));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Billing] read-only reassurance failed: {ex.Message}");
		}
	}

	protected override void OnSleep()
	{
		base.OnSleep();
		// A backgrounded app must not keep polling the network.
		_cloudSync.NotifyAppState(foreground: false);

		// Last moment we can be certain the app was alive. The boot catch-up treats
		// everything after this marker as "could not have fired while the device was
		// off"; without stamping it here the marker only moved at cold start, so a
		// carer who used the app all week and then rebooted got a week-wide window and
		// a burst of missed-dose alerts for doses that had fired normally.
		MedicationReminderScheduler.MarkSeen();
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
			// re-sending here would duplicate them. A genuine device-off gap comes
			// back through the boot receiver, which records that intent durably, so
			// this pass still honours it if it happens to run first (starting the
			// process after a reboot runs this path too).
			// Runs off the UI path so startup isn't blocked.
			_ = Task.Run(async () =>
			{
				try
				{
					// Channels must exist before anything is posted to them, and
					// re-registering refreshes their names in the applied language.
					await _notifications.EnsureChannelsAsync();

					await _reminderScheduler.CatchUpAndRefreshAsync(resendMissed: false);
					// Arm/refresh today's daily care reminder for each pet (no-op when off).
					await _dailyReminderScheduler.RefreshAsync();
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

			// Billing: initialize the entitlement boundary and, for an already-onboarded
			// user (pets exist) landing on this build, start the trial clock if it hasn't
			// begun — new users start theirs at onboarding completion (KeepSafePage). All
			// off the UI path and a quiet no-op under the Null boundary.
			var hasPets = pets.Count > 0;
			_ = Task.Run(async () =>
			{
				try
				{
					await _entitlements.InitializeAsync();
					if (hasPets && await _entitlements.EnsureTrialStartedAsync())
						_analytics.Track(AnalyticsEvents.TrialStarted);
					await MaybeShowReadOnlyReassuranceAsync();
					await MaybeShowPreEndNudgeAsync();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[Billing] launch init failed: {ex.Message}");
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
