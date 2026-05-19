namespace Animal_Diary_App;

using Animal_Diary_App.Data.View;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(PetsPage), typeof(PetsPage));
		Routing.RegisterRoute(nameof(PetTypePage), typeof(PetTypePage));
		Routing.RegisterRoute(nameof(WelcomePage), typeof(WelcomePage));
		Routing.RegisterRoute(nameof(MainPage), typeof(MainPage));
		Routing.RegisterRoute(nameof(CalendarPage), typeof(CalendarPage));
	}
}
