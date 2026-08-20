using Microsoft.Extensions.Logging;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Attribution;
using Animal_Diary_App.Data.Services.Billing;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Data.Services.Reports;
using Animal_Diary_App.Data.View;
using Animal_Diary_App.Data.ViewModels;
using Plugin.LocalNotification;

// Plugin.LocalNotification also declares an INotificationService. Ours is the app's own
// device boundary, so alias it rather than importing the folder and living with the
// ambiguity — the plugin's type is never named here, only .UseLocalNotification().
using INotificationService = Animal_Diary_App.Data.Services.Data.Device.INotificationService;

namespace Animal_Diary_App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseLocalNotification()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("Fraunces.ttf", "Fraunces");
				fonts.AddFont("Fraunces-Italic.ttf", "FrauncesItalic");
				fonts.AddFont("PlusJakartaSans-Regular.ttf", "PlusJakartaSans");
				fonts.AddFont("Caveat.ttf", "Caveat");
			});

		// ── ViewModels ───────────────────────────────────────────────────────
		// All singletons: MainViewModel composes them and every page binds to the
		// same instance, so form drafts and loaded lists survive a tab switch.
		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<PetViewModel>();
		builder.Services.AddSingleton<CalendarViewModel>();
		builder.Services.AddSingleton<MedicationViewModel>();
		builder.Services.AddSingleton<ConditionPickerViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MoodTimelineViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<ManagePetViewModel>();
		builder.Services.AddSingleton<JournalLogViewModel>();
		builder.Services.AddSingleton<ConstellationViewModel>();

		// Journal input sheets (one VM per loggable type).
		builder.Services.AddSingleton<GlucoseSheetViewModel>();
		builder.Services.AddSingleton<MoodSheetViewModel>();
		builder.Services.AddSingleton<WeightSheetViewModel>();
		builder.Services.AddSingleton<AppetiteSheetViewModel>();
		builder.Services.AddSingleton<SeizureSheetViewModel>();
		builder.Services.AddSingleton<WaterSheetViewModel>();
		builder.Services.AddSingleton<CustomEntrySheetViewModel>();

		// Today's stat-card picker (which record each of the two cards shows).
		builder.Services.AddSingleton<TodayCardSheetViewModel>();
		builder.Services.AddSingleton<VetQuestionSheetViewModel>();

		// Condition-setup sheets, shared by onboarding and the Manage page.
		builder.Services.AddSingleton<DiabetesSetupSheetViewModel>();
		builder.Services.AddSingleton<CkdSetupSheetViewModel>();
		builder.Services.AddSingleton<EpilepsySetupSheetViewModel>();
		builder.Services.AddSingleton<CustomTrackerSheetViewModel>();

		// Vet-report surfaces, cloud/billing sheets, and the hidden dev panel.
		builder.Services.AddSingleton<ExportSheetViewModel>();
		builder.Services.AddSingleton<ReportPreviewViewModel>();
		builder.Services.AddSingleton<DocumentsViewModel>();
		builder.Services.AddSingleton<CloudSheetViewModel>();
		builder.Services.AddSingleton<SharingSheetViewModel>();
		builder.Services.AddSingleton<SubscribeSheetViewModel>();
		builder.Services.AddSingleton<TrialMessageViewModel>();
		builder.Services.AddSingleton<RedeemCodeSheetViewModel>();
		builder.Services.AddSingleton<FeedbackSheetViewModel>();
		builder.Services.AddSingleton<ConfirmSheetViewModel>();
		// Crop + rotate, between picking a photo and it becoming a pet's avatar.
		builder.Services.AddSingleton<PhotoEditorSheetViewModel>();
		builder.Services.AddSingleton<DevSheetViewModel>();
		// The AI entry importer, reached from the dev sheet behind its own code.
		builder.Services.AddSingleton<ImportViewModel>();

		// ── Data / SQLite ────────────────────────────────────────────────────
		builder.Services.AddSingleton<AppDatabase>();
		builder.Services.AddSingleton<PetService>();
		builder.Services.AddSingleton<PetEntryService>();
		builder.Services.AddSingleton<PetPauseService>();
		builder.Services.AddSingleton<PetPhotoService>();
		builder.Services.AddSingleton<PetDeletionService>();
		// The local-only hard delete, shared by revoked-access purges and leaving demo mode.
		builder.Services.AddSingleton<PetPurgeService>();
		builder.Services.AddSingleton<MedicationService>();
		builder.Services.AddSingleton<MedicationDoseLogService>();
		builder.Services.AddSingleton<DayDoseService>();
		builder.Services.AddSingleton<ActivePetService>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<AppResetService>();

		// ── Journal (care plan, pending engine inputs, typed entry stores) ───
		builder.Services.AddSingleton<GlucoseEntryService>();
		builder.Services.AddSingleton<AppetiteEntryService>();
		builder.Services.AddSingleton<SeizureEntryService>();
		builder.Services.AddSingleton<WaterEntryService>();
		builder.Services.AddSingleton<CustomTrackerService>();
		builder.Services.AddSingleton<TrackerService>();
		builder.Services.AddSingleton<PetConditionService>();
		builder.Services.AddSingleton<CarePlanService>();
		builder.Services.AddSingleton<PendingItemsService>();
		builder.Services.AddSingleton<TodayCardService>();
		builder.Services.AddSingleton<ConstellationService>();
		// Facts about the record — one computation, three surfaces (Today's card
		// sheet, the Constellation legend, the appointment summary).
		builder.Services.AddSingleton<RecordFactsService>();
		builder.Services.AddSingleton<VetQuestionService>();

		// ── Import (AI-written entry files) ──────────────────────────────────
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Import.ImportService>();

		// ── Demo data (the seeded creator pets) ──────────────────────────────
		// Registered on every build: a creator holds a store build, so unlike the
		// fixtures this replaces it cannot be a compile-time switch. Nothing happens
		// until the demo section of the dev sheet seeds it.
		builder.Services.AddSingleton<Data.Services.Demo.DemoModeService>();

		// ── Reports (vet PDF + the library around it) ────────────────────────
		builder.Services.AddSingleton<VetReportDataBuilder>();
		builder.Services.AddSingleton<ReportLibraryService>();
		builder.Services.AddSingleton<IVetReportService, VetReportService>();
		// Preview rasterizer: each platform uses its OS PDF renderer (no native library).
		// iOS/macOS fall back to the no-op — the PDF still generates, just without previews.
#if ANDROID
		builder.Services.AddSingleton<IPdfPageRasterizer, AndroidPdfPageRasterizer>();
#elif WINDOWS
		builder.Services.AddSingleton<IPdfPageRasterizer, WindowsPdfPageRasterizer>();
#else
		builder.Services.AddSingleton<IPdfPageRasterizer, NoOpPdfPageRasterizer>();
#endif

		// ── Notifications ────────────────────────────────────────────────────
		builder.Services.AddSingleton<INotificationService, Data.Services.Data.Device.NotificationService>();

		// Install referrer: Android reads Google Play's, every other platform has no such
		// concept (Apple's campaign tokens never reach the app), so they get the null source
		// and creator attribution there depends entirely on the typed code.
#if ANDROID
		builder.Services.AddSingleton<Data.Services.Data.Device.IInstallReferrerSource,
			Platforms.Android.AndroidInstallReferrerSource>();
#else
		builder.Services.AddSingleton<Data.Services.Data.Device.IInstallReferrerSource,
			Data.Services.Data.Device.NullInstallReferrerSource>();
#endif
		// ── Ad attribution boundary (mirrors the analytics/cloud/billing boundaries) ──
		// Reports the install to Meta so an app-promotion campaign can attribute it, and
		// nothing else — no events, no properties, no user or pet data ever reaches it (see
		// AI/analytics.md for the boundary, which is a product rule, not a style choice).
		//
		// Two conditions, both required: an Android build with the SDK binding, AND Meta
		// enabled with credentials present. Anything else resolves the no-op, which also
		// hides the Settings toggle. iOS is deliberately on the no-op for now — the app
		// isn't on the App Store yet and Meta's iOS SDK is a separate binding.
#if ANDROID
		if (MetaAdsConfig.Enabled && MetaAdsConfig.IsConfigured)
		{
			builder.Services.AddSingleton<IAdAttributionService,
				Platforms.Android.MetaAdAttributionService>();
		}
		else
		{
			builder.Services.AddSingleton<IAdAttributionService, NullAdAttributionService>();
		}
#else
		builder.Services.AddSingleton<IAdAttributionService, NullAdAttributionService>();
#endif

		builder.Services.AddSingleton<ReminderInstanceService>();
		builder.Services.AddSingleton<MedicationDoseReconciler>();
		builder.Services.AddSingleton<MedicationReminderScheduler>();
		builder.Services.AddSingleton<DailyCareReminderScheduler>();

		builder.Services.AddSingleton<App>();

		// ── Analytics boundary ───────────────────────────────────────────────
		// One line decides the whole posture: a real PostHog sender when analytics is
		// enabled AND a project key is configured, else a no-op that collects and sends
		// nothing. Every consumer holds IAnalyticsService only.
		if (AnalyticsConfig.Enabled)
		{
			// Backing store for the offline retry queue. Registered only on this branch:
			// with analytics off there is nothing to buffer, and the no-op sender must not
			// so much as touch the file system.
			builder.Services.AddSingleton<IAnalyticsQueueStore, FileAnalyticsQueueStore>();
			builder.Services.AddSingleton<IAnalyticsService, PostHogAnalyticsService>();
		}
		else
		{
			builder.Services.AddSingleton<IAnalyticsService, NullAnalyticsService>();
		}

		// ── Cloud boundary (mirrors the analytics boundary) ──────────────────
		// The real sync engine when cloud features are compiled in, else a no-op.
		// Everything holds ICloudSyncService / ICloudAuthService only — no Supabase
		// types escape Data/Services/Cloud/.
		//
		// The trial clock is registered on EVERY platform, ahead of the cloud block: the
		// sync engine reconciles this device's trial anchor with the account (so the server
		// can tell a caregiver whether their pet's owner is still in trial), and cloud
		// registration is not platform-gated. Harmless where billing is off — the Null
		// entitlement service ignores it, so Windows/macOS dev still never locks.
		builder.Services.AddSingleton<ITrialStore>(sp => sp.GetRequiredService<SettingsService>());
		builder.Services.AddSingleton<TrialService>();
		builder.Services.AddSingleton<ITrialAnchor>(sp => sp.GetRequiredService<TrialService>());

		builder.Services.AddSingleton<CloudHttp>();
		builder.Services.AddSingleton<SyncStateStore>();
		builder.Services.AddSingleton<ICloudAuthService, CloudAuthService>();
		if (CloudConfig.Enabled)
		{
			builder.Services.AddSingleton<ICloudSyncService, CloudSyncService>();
			builder.Services.AddSingleton<ICloudSharingService, CloudSharingService>();
			builder.Services.AddSingleton<CloudAccessCodeService>();
			builder.Services.AddSingleton<ICloudAccessCodeService>(sp => sp.GetRequiredService<CloudAccessCodeService>());
			builder.Services.AddSingleton<ICloudReferralService, CloudReferralService>();
		}
		else
		{
			builder.Services.AddSingleton<ICloudSyncService, NullCloudSyncService>();
			builder.Services.AddSingleton<ICloudSharingService, NullCloudSharingService>();
			builder.Services.AddSingleton<NullCloudAccessCodeService>();
			builder.Services.AddSingleton<ICloudAccessCodeService>(sp => sp.GetRequiredService<NullCloudAccessCodeService>());
			builder.Services.AddSingleton<ICloudReferralService, NullCloudReferralService>();
		}

		// Billing reads sponsorship through the cloud engine, but only ever as the pure
		// IPetAccessSource — the Billing folder must not learn about Supabase. Both sync
		// implementations provide it, so this resolves in either branch above.
		builder.Services.AddSingleton<IPetAccessSource>(sp =>
			(IPetAccessSource)sp.GetRequiredService<ICloudSyncService>());

		// Same shape for redeemed access codes: Billing declares IGrantSource, Cloud
		// implements it. Registered against the concrete type rather than ICloudAccessCodeService
		// so both faces of the one singleton share a cache.
		builder.Services.AddSingleton<IGrantSource>(sp =>
			(IGrantSource)sp.GetRequiredService<ICloudAccessCodeService>());

		// ── Billing / monetization boundary ──────────────────────────────────
		// Mirrors the cloud & analytics boundaries: the real trial + entitlement service
		// only on a mobile store AND when a key + binding are wired (BillingConfig.Enabled);
		// otherwise a no-op that grants full access. Windows/macOS dev always gets the
		// no-op, so it can never lock.
#if ANDROID || IOS
		if (BillingConfig.Enabled)
		{
			// The RevenueCat binding's own DI (registers IRevenueCatBilling), then our
			// seam over it and the composed entitlement gate.
			Maui.RevenueCat.InAppBilling.RevenueCatBillingInstaller.AddRevenueCatBilling(builder.Services);
			builder.Services.AddSingleton<IStoreBilling, RevenueCatStoreBilling>();
			builder.Services.AddSingleton<IEntitlementService, EntitlementService>();
		}
		else
		{
			builder.Services.AddSingleton<IEntitlementService, NullEntitlementService>();
		}
#elif DEBUG
		// Desktop normally gets the no-op so development can never be locked out. That also
		// makes a desktop useless as a caregiver TEST device: NullEntitlementService reports
		// Subscribed for every account, so every gate check passes without exercising one.
		// This opt-in runs the real gate over a no-op store — access from trial or
		// sponsorship only, purchases unavailable.
		if (BillingConfig.ForceGateOnDesktop)
		{
			builder.Services.AddSingleton<IStoreBilling, NullStoreBilling>();
			builder.Services.AddSingleton<IEntitlementService, EntitlementService>();
		}
		else
		{
			builder.Services.AddSingleton<IEntitlementService, NullEntitlementService>();
		}
#else
		builder.Services.AddSingleton<IEntitlementService, NullEntitlementService>();
#endif

		// ── Shell + its three tab pages ──────────────────────────────────────
		// Transient so a post-reset relaunch builds a fresh Shell (with fresh page
		// instances); within one Shell each page is still constructed once and reused
		// across tab switches. Pushed and onboarding pages are NOT registered — they are
		// constructed directly with the shared MainViewModel (see AI/architecture.md).
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<CalendarPage>();
		builder.Services.AddTransient<PetsPage>();
		builder.Services.AddTransient<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
