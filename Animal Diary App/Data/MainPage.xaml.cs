namespace Animal_Diary_App.Data;

public partial class MainPage : ContentPage
{
	int count = 0;

	public MainPage()
	{
		InitializeComponent();
	}

	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string AnimalName = entry.Text;
		await Shell.Current.GoToAsync(nameof(PetAgePage));
	}
}
