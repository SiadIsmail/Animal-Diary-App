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
	private readonly ICloudAuthService _cloudAuth;
	private readonly Animal_Diary_App.Data.Services.Billing.IEntitlementService _entitlements;
	private readonly MedicationDoseLogService _doseLogs;
	private readonly IServiceProvider _services;

	public App(PetService petService, MainViewModel vm, AppDatabase database, ActivePetService activePetService, MedicationReminderScheduler reminderScheduler, DailyCareReminderScheduler dailyReminderScheduler, Animal_Diary_App.Data.Services.Data.Device.INotificationService notifications, SettingsService settingsService, IAnalyticsService analytics, ICloudSyncService cloudSync, ICloudAuthService cloudAuth, Animal_Diary_App.Data.Services.Billing.IEntitlementService entitlements, MedicationDoseLogService doseLogs, IServiceProvider services)
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
		_cloudAuth = cloudAuth;
		_entitlements = entitlements;
		_doseLogs = doseLogs;
		_services = services;

		// Keep the store identity on the signed-in account, so one subscription follows the
		// person across devices instead of being stranded on the install that bought it.
		// Signing in is never required to buy or to keep access — with no account the store
		// simply stays anonymous, exactly as before.
		_cloudAuth.SessionChanged += OnCloudSessionChanged;

		// A caregiver's cover can end mid-session when a sync lands (the owner lapsed, or
		// their trial ran out). Say so once rather than letting the app quietly stop
		// accepting entries.
		_cloudSync.SponsorshipRevoked += OnSponsorshipRevoked;

		// Re-engagement signal: the app was foregrounded by tapping a medication
		// reminder. This is the ONLY place the notification-tap hook is used for
		// analytics; it carries no notification content, just the fact of a tap.
		LocalNotificationCenter.Current.NotificationActionTapped += OnNotificationTapped;

		_ = StartAsync();
	}

	/// <summary>Sign-in/out: alias the store identity onto the account, or back off it.
	/// Fire-and-forget — nothing about signing in should wait on the store.</summary>
	private void OnCloudSessionChanged() => _ = Task.Run(async () =>
	{
		try { await _entitlements.IdentifyAsync(_cloudAuth.UserId); }
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Billing] identify failed: {ex.Message}"); }
	});

	/// <summary>A pet this user was caring for under someone else's subscription is no
	/// longer covered. Tell them once, naming the real reason — they never had a trial of
	/// their own, so "your trial has ended" would be false. Fires per transition, not once
	/// forever: if the owner resubscribes and lapses again, this is worth saying again.</summary>
	private void OnSponsorshipRevoked(IReadOnlyList<string> petSyncIds)
	{
		if (petSyncIds.Count == 0)
			return;
		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				// Name the pet only when it is the one on screen; otherwise stay general
				// rather than pulling an off-screen pet into view.
				var active = _vm.PetVM.ActivePet;
				var name = active != null && petSyncIds.Contains(active.SyncId) ? active.Name : string.Empty;
				_vm.TrialMessageVM.ShowSponsorshipEnded(name);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Billing] sponsorship notice failed: {ex.Message}");
			}
		});
	}

	private void OnNotificationTapped(Plugin.LocalNotification.EventArgs.NotificationActionEventArgs e)
	{
		// Guard: analytics must never break a user gesture.
		try { _analytics.Track(AnalyticsEvents.NotificationOpened); }
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Analytics] notification_opened failed: {ex.Message}"); }
	}

	// ── Root-page publication ────────────────────────────────────────────────────
	// Startup and window creation are two independent events with no guaranteed
	// order, and on Android window creation happens MORE THAN ONCE per process. The
	// LoadingPage used to be a dead end under both facts, so this pair exists to make
	// "what the app should be showing" a value that either side can pick up:
	//
	//  • StartAsync publishes a factory once it has decided the root.
	//  • CreateWindow uses it if it's already there, otherwise applies it on arrival.
	//
	// A factory, not a Page: MAUI recreates the Activity (and calls CreateWindow
	// again) on memory pressure, on "don't keep activities", and on config changes
	// this app doesn't declare — and a Page instance can only belong to one Window.

	// The factory is a mutable field and the TCS only signals "startup has decided",
	// because the root changes again afterwards (the language picker hands over to the
	// real root). A TaskCompletionSource<Func<Page>> cannot express that — TrySetResult
	// on an already-completed source is a silent no-op, which would send a recreated
	// window back to the language picker forever.
	private Func<Page>? _rootFactory;
	private readonly TaskCompletionSource _rootReady =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>The current root, or the error page if startup never published one.</summary>
	private Page BuildCurrentRoot() => (_rootFactory ?? BuildErrorPage)();

	/// <summary>Whether onboarding is behind us, read by <see cref="BuildRootPage"/>.
	/// A field rather than a captured local: <see cref="CreateWindow"/> can run hours
	/// after startup, and by then "did this user have pets at launch" is the wrong
	/// question — a pet added during the session, or a data reset, moves this.</summary>
	private bool _hasPets;

	/// <summary>The window to navigate. Deliberately not <c>Windows[0]</c>: Android can
	/// add a second <see cref="Window"/> when the Activity is recreated, and the first
	/// entry is then the detached one, so writing to it changes nothing on screen.</summary>
	private static Window? ActiveWindow => Application.Current?.Windows.LastOrDefault();

	/// <summary>The real root for the current state. Called once per Window, so every
	/// window gets its own page instances (the pages and the Shell are transient).</summary>
	private Page BuildRootPage() => _hasPets
		? _services.GetRequiredService<AppShell>()
		: new NavigationPage(new WelcomePage(_vm));

	private static Page BuildErrorPage() => new ContentPage
	{
		Content = new Label
		{
			Text = LocalizationManager.Instance.GetString("App_StartError"),
			Margin = 24,
			HorizontalTextAlignment = TextAlignment.Center,
			VerticalTextAlignment = TextAlignment.Center
		}
	};

	/// <summary>
	/// Swap the window root to the tabbed <see cref="AppShell"/>. Called when the
	/// user leaves onboarding (first pet saved). A fresh Shell is resolved so a
	/// post-reset relaunch doesn't reuse stale page instances.
	/// </summary>
	public void SwitchToMainApp()
	{
		// Recorded before the swap so a later Activity recreation rebuilds the Shell
		// rather than dropping the user back on the welcome screen. Both fields are
		// set explicitly so this is correct however the caller got here (in practice
		// the language picker has already handed over).
		_hasPets = true;
		_rootFactory = BuildRootPage;
		_rootReady.TrySetResult();
		if (ActiveWindow is Window window)
			window.Page = _services.GetRequiredService<AppShell>();
	}

	/// <summary>Return to onboarding after a full data reset. Routed through here so the
	/// "no pets" state is recorded for any later window, not just applied to this one.</summary>
	public void SwitchToOnboarding()
	{
		_hasPets = false;
		_rootFactory = BuildRootPage;
		_rootReady.TrySetResult();
		if (ActiveWindow is Window window)
			window.Page = new NavigationPage(new WelcomePage(_vm));
	}

	/// <summary>
	/// Record <c>app_opened</c> if this appearance begins a new session.
	///
	/// <para>Called only from touchpoints that <b>require a real Activity</b> — window
	/// creation and resume. That is the fix for the headless launch: a reboot or a Play
	/// Store update starts the process through the boot receiver (it resolves services,
	/// which constructs <c>App</c> and runs <c>StartAsync</c>) with no window and nobody
	/// looking at it, and those were being counted as launches.</para>
	///
	/// <para>The session gate then handles the other direction. Firing on every one of
	/// these touchpoints would over-count — Android recreates the Activity on memory
	/// pressure and configuration changes, and resume fires on every app-switch — while
	/// firing once per process under-counts, because a process can survive a week of daily
	/// use. An idle window (<see cref="AnalyticsSession.IdleTimeout"/>) counts one open per
	/// visit, which is what the funnel's "came back later" step reads.</para>
	/// </summary>
	private void TrackSessionStart()
	{
		try
		{
			if (!AnalyticsIdentity.TryBeginSession())
				return;

			_analytics.Track(AnalyticsEvents.AppOpened, new Dictionary<string, object?>
			{
				[AnalyticsEvents.PropLanguage] = LocalizationManager.Instance.CurrentLanguage,
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Analytics] app_opened failed: {ex.Message}");
		}
	}

	private int _windowsCreated;

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Breadcrumb: a second invocation in one process means the Activity was
		// recreated, which is the case that used to strand the user on LoadingPage.
		System.Diagnostics.Debug.WriteLine(
			$"[Startup] CreateWindow #{++_windowsCreated}, root ready: {_rootReady.Task.IsCompleted}");

		// Startup already finished. App is a singleton, so StartAsync will NOT run
		// again to navigate away from a loading screen — build the real root now.
		if (_rootReady.Task.IsCompletedSuccessfully)
		{
			// A window exists and the language is applied, so this is a countable open —
			// unless the session gate says we're still inside the previous one, which is
			// what makes an Activity recreation free.
			TrackSessionStart();

			try
			{
				return new Window(BuildCurrentRoot());
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Startup] root rebuild failed: {ex}");
				return new Window(BuildErrorPage());
			}
		}

		// First launch. Show the loading screen and apply the root the moment startup
		// publishes it — whichever of the two gets there first.
		var loadingWindow = new Window(new LoadingPage());
		_rootReady.Task.ContinueWith(
			_ => MainThread.BeginInvokeOnMainThread(() =>
			{
				try
				{
					loadingWindow.Page = BuildCurrentRoot();
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"[Startup] root apply failed: {ex}");
					loadingWindow.Page = BuildErrorPage();
				}

				// Counted here rather than beside the loading window: startup has now
				// chosen and applied the language, so app_opened carries the real one.
				// Fired even if the root failed — the user did open the app, and hiding
				// broken launches from the funnel would hide the breakage with them.
				TrackSessionStart();
			}),
			TaskScheduler.Default);

		return loadingWindow;
	}

	protected override void OnResume()
	{
		base.OnResume();

		// The return path into the app. Only counts when the user has been away longer
		// than the session window, so an app-switch to read a text stays one visit while
		// coming back tomorrow is a new open — the event the funnel's "came back later"
		// step reads.
		TrackSessionStart();

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
			// This copy says the trial has ended, so it is only ever true for someone who
			// had one. A caregiver who only tends another person's pet never started a
			// trial; when their cover ends they get the sponsorship message instead.
			if (!_entitlements.TrialEverStarted)
				return;
			if (await _settingsService.GetFlagAsync(SettingsFlags.ReadOnlyReassuranceShown))
				return;
			await _settingsService.SetFlagAsync(SettingsFlags.ReadOnlyReassuranceShown, true);

			var petName = _vm.PetVM.ActivePet?.Name ?? string.Empty;
			var trialDay = (int)Animal_Diary_App.Data.Services.Billing.BillingConfig.TrialLength.TotalDays;

			// If they share a pet, their carers just went read-only too. The owner is the
			// only person who can change that, and hearing it here beats discovering it
			// when someone else can't log a dose.
			var hasCaregivers = _cloudSync.OwnsASharedPet;

			MainThread.BeginInvokeOnMainThread(
				() => _vm.TrialMessageVM.ShowReadOnly(petName, trialDay, hasCaregivers));
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Billing] read-only reassurance failed: {ex.Message}");
		}
	}

	protected override void OnSleep()
	{
		base.OnSleep();

		// Start the analytics idle clock from the moment the user actually left, not from
		// whenever the last event happened — otherwise a long read-only session would age
		// out while still on screen and count the next glance as a new open. Distinct from
		// MedicationReminderScheduler.MarkSeen below: same instant, unrelated purpose.
		try { AnalyticsIdentity.TouchActivity(); }
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Analytics] activity stamp failed: {ex.Message}"); }

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

			// Drives BuildRootPage from here on. The post-onboarding landing page is
			// built lazily (inside that factory) so it inflates *after* the chosen
			// language has been applied.
			_hasPets = pets.Count > 0;

			var savedLanguage = await _settingsService.GetLanguageAsync();

			if (savedLanguage != null)
			{
				// Returning user: apply their saved language and go straight in.
				LocalizationManager.Instance.SetLanguage(savedLanguage);
				_rootFactory = BuildRootPage;
			}
			else
			{
				// First launch: seed the UI culture from the device (so the picker
				// reads naturally) then ask the user which language they want.
				var deviceLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en";
				LocalizationManager.Instance.SetLanguage(deviceLanguage);

				_rootFactory = () => new LanguageSelectionPage(_settingsService, code =>
				{
					// The picker is done: from now on the root is the real one, for
					// this window and for any window a later recreation builds.
					_rootFactory = BuildRootPage;
					if (ActiveWindow is Window window)
						window.Page = BuildRootPage();
				});
			}

			// Publish rather than navigate. The window may not exist yet (nothing
			// orders StartAsync against CreateWindow), and it may be replaced later.
			_rootReady.TrySetResult();

			// Analytics: prepare the anonymous id and flush anything the last session
			// couldn't deliver. Deliberately does NOT record the launch — see
			// TrackSessionStart: this method also runs in the headless process a reboot or
			// an app update starts, where there is no window and no user to count. Wrapped
			// defensively — telemetry must never affect startup.
			try
			{
				await _analytics.InitializeAsync();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Analytics] init failed: {ex.Message}");
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
			// user landing on this build, start the trial clock if it hasn't begun — new
			// users start theirs at onboarding completion (KeepSafePage). All off the UI
			// path and a quiet no-op under the Null boundary.
			//
			// The trial begins with a pet you OWN. Caring for someone else's animal never
			// starts your clock: a caregiver is covered by that owner's subscription while
			// it lasts, and starting a trial for them here would burn it on a record they
			// don't own and hand them a paywall the owner is already paying to avoid.
			var petsAtLaunch = pets;
			_ = Task.Run(async () =>
			{
				try
				{
					await _entitlements.InitializeAsync();
					// Idempotent — just re-reads persisted state; the launch sync above may
					// not have got here yet and ownership comes from that cached role map.
					await _cloudSync.InitializeAsync();
					var ownsAPet = petsAtLaunch.Any(p =>
						_cloudSync.GetPetRole(p.SyncId ?? string.Empty) != "caregiver");
					if (ownsAPet && await _entitlements.EnsureTrialStartedAsync())
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
			// Published, not assigned: a throw before the window exists used to hit the
			// same silent guard as the success path, so the failure showed up as a
			// permanent loading screen instead of a message the user could act on.
			_rootFactory = BuildErrorPage;
			_rootReady.TrySetResult();
		}
	}
}
