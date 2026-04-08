namespace Animal_Diary_App.Data;

public partial class PetAgePage : ContentPage
{
	int count = 0;

	public PetAgePage()
	{
		InitializeComponent();
	}

	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string animalName = entry.Text;
		await Shell.Current.GoToAsync(nameof(PetAgePage));
	}
}
