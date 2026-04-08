namespace Animal_Diary_App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.Register(nameof(PetAgePage), typeof(Animal_Diary_App.Data.PetAgePage));
		Routing.Register(nameof(PetTypePage), typeof(Animal_Diary_App.Data.PetTypePage));
	}
}
