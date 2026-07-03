using Microsoft.Extensions.Logging;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Syncfusion.Maui.Core.Hosting;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;
using Plugin.LocalNotification;
namespace Animal_Diary_App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.ConfigureSyncfusionCore();
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
			});

		builder.Services.AddSingleton<MainViewModel>();
		builder.Services.AddSingleton<PetViewModel>();
		builder.Services.AddSingleton<CalendarViewModel>();
		builder.Services.AddSingleton<MedicationViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<MoodTimelineViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddSingleton<AppDatabase>();
		builder.Services.AddSingleton<PetEntryService>();
		builder.Services.AddSingleton<PetService>();
		builder.Services.AddSingleton<MedicationService>();
		builder.Services.AddSingleton<MedicationDoseLogService>();
		builder.Services.AddSingleton<ActivePetService>();
		builder.Services.AddSingleton<SettingsService>();
		builder.Services.AddSingleton<AppResetService>();
		builder.Services.AddSingleton<App>();
		builder.Services.AddSingleton<Animal_Diary_App.Data.Services.Data.Device.INotificationService, NotificationService>();
		builder.Services.AddSingleton<ReminderInstanceService>();
		builder.Services.AddSingleton<MedicationDoseReconciler>();
		builder.Services.AddSingleton<MedicationReminderScheduler>();


#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
