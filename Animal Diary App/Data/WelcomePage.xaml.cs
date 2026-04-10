namespace Animal_Diary_App.Data;

public partial class WelcomePage : ContentPage
{

	public WelcomePage()
	{
		InitializeComponent();
	}

	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string AnimalName = entry.Text;
		await Shell.Current.GoToAsync(nameof(PetAgePage));
	}
}
