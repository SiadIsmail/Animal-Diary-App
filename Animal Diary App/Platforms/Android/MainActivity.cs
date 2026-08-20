using Android.App;
using Android.Content.PM;
using Android.OS;
using Animal_Diary_App.Data.Services.Attribution;

namespace Animal_Diary_App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		// Meta install attribution, if the owner allows it. Deliberately here rather than
		// in App.StartAsync: StartAsync also runs headlessly on a reboot or a Play Store
		// update (the boot receiver resolves services, which constructs App), and reporting
		// an app launch nobody performed is exactly the error that moved app_opened onto
		// the window touchpoints. An Activity means a real person opened the app.
		//
		// Idempotent by design, because this runs again on every Activity recreation
		// (memory pressure, font-size and language changes): Start() no-ops once the SDK is
		// up, and the SDK itself decides install-vs-activation, so a recreation cannot
		// invent a second install. base.OnCreate first, so DI exists by the time we resolve.
		try
		{
			IPlatformApplication.Current?.Services
				.GetService<IAdAttributionService>()?.Start();
		}
		catch (Exception ex)
		{
			// Never let attribution break a launch. Same fail-safe rule as analytics.
			System.Diagnostics.Debug.WriteLine($"[MetaAds] start failed: {ex.Message}");
		}
	}
}
