using Microsoft.Extensions.Logging;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;
using Plugin.LocalNotification;
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

		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<PetViewModel>();
		builder.Services.AddSingleton<CalendarViewModel>();
		builder.Services.AddSingleton<MedicationViewModel>();
		builder.Services.AddSingleton<ConditionPickerViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MoodTimelineViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<AppDatabase>();
		builder.Services.AddSingleton<PetEntryService>();
		builder.Services.AddSingleton<TrackingEntryService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.GlucoseEntryService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.AppetiteEntryService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.SeizureEntryService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.TrackerService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.PetConditionService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.CarePlanService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Journal.PendingItemsService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Reports.VetReportDataBuilder>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Reports.ReportLibraryService>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Reports.IVetReportService, Animal_Diary_App.Data.Services.Reports.VetReportService>();
		builder.Services.AddSingleton<ExportSheetViewModel>();
		builder.Services.AddSingleton<ReportPreviewViewModel>();
		builder.Services.AddSingleton<DocumentsViewModel>();
		builder.Services.AddSingleton<GlucoseSheetViewModel>();
		builder.Services.AddSingleton<MoodSheetViewModel>();
		builder.Services.AddSingleton<WeightSheetViewModel>();
		builder.Services.AddSingleton<AppetiteSheetViewModel>();
		builder.Services.AddSingleton<SeizureSheetViewModel>();
		builder.Services.AddSingleton<DiabetesSetupSheetViewModel>();
		builder.Services.AddSingleton<CkdSetupSheetViewModel>();
		builder.Services.AddSingleton<EpilepsySetupSheetViewModel>();
		builder.Services.AddSingleton<ManagePetViewModel>();
		builder.Services.AddSingleton<JournalLogViewModel>();
		builder.Services.AddSingleton<PetService>();
		builder.Services.AddSingleton<MedicationService>();
		builder.Services.AddSingleton<MedicationDoseLogService>();
		builder.Services.AddSingleton<DayDoseService>();
		builder.Services.AddSingleton<ActivePetService>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<AppResetService>();
		builder.Services.AddSingleton<App>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Data.Device.INotificationService, NotificationService>();
		builder.Services.AddSingleton<ReminderInstanceService>();
		builder.Services.AddSingleton<MedicationDoseReconciler>();
		builder.Services.AddSingleton<MedicationReminderScheduler>();

		// Shell + its three tab pages. Transient so a post-reset relaunch builds a
		// fresh Shell (with fresh page instances); within one Shell each page is
		// still constructed once and reused across tab switches.
		builder.Services.AddTransient<Animal_Diary_App.Data.View.MainPage>();
		builder.Services.AddTransient<Animal_Diary_App.Data.View.CalendarPage>();
		builder.Services.AddTransient<Animal_Diary_App.Data.View.PetsPage>();
		builder.Services.AddTransient<AppShell>();


#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
