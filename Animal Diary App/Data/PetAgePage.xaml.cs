namespace Animal_Diary_App.Data;

public partial class PetAgePage : ContentPage
{
	

	public PetAgePage()
	{
		InitializeComponent();
	}

	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string PetAge = entry.Text;
		await Shell.Current.GoToAsync(nameof(PetTypePage));
	}
}
