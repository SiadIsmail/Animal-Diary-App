using Microsoft.Extensions.Logging;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Syncfusion.Maui.Core.Hosting;

namespace Animal_Diary_App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.ConfigureSyncfusionCore();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<PetViewModel>();
		builder.Services.AddSingleton<CalendarViewModel>();
		builder.Services.AddSingleton<MainPageViewModel>();
		builder.Services.AddSingleton<PetDatabase>();


#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
