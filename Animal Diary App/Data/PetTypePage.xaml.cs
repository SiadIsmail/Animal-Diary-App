namespace Animal_Diary_App.Data;

public partial class PetTypePage : ContentPage
{


	public PetTypePage()
	{
		InitializeComponent();
	}
	
	private async void OnEntryCompleted(object sender, EventArgs e)
	{
		string PetType = entry.Text;
		await Shell.Current.GoToAsync(nameof(MainPage));
	}




}
