namespace Animal_Diary_App;

using Animal_Diary_App.Data;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(PetAgePage), typeof(PetAgePage));
		Routing.RegisterRoute(nameof(PetTypePage), typeof(PetTypePage));
		Routing.RegisterRoute(nameof(WelcomePage), typeof(WelcomePage));
		
	}
}
