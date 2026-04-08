namespace Animal_Diary_App;

public partial class MainPage : ContentPage
{
	int count = 0;

	public MainPage()
	{
		InitializeComponent();
	}

	private void OnEntryCompleted(object sender, EventArgs e)
	{
		string animalName = entry.Text;
		await Shell.Current.GoToAsync("NewPage");
	}
}
