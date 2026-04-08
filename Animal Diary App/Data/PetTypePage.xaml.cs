namespace Animal_Diary_App.Data;

public partial class PetTypePage : ContentPage
{
	int count = 0;

	public PetTypePage()
	{
		InitializeComponent();
	}

	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string animalName = entry.Text;
		
	}
}
