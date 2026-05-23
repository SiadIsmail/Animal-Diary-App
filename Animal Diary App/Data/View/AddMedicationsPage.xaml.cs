using Animal_Diary_App.Data.ViewModels;
namespace Animal_Diary_App.Data.View;

public partial class AddEditMedicationsPage : ContentPage
{
    private MainViewModel vm;
    private CalendarPage? calendarPage;
    private PetsPage? petPage;

    public AddEditMedicationsPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    async void OnBackClicked(object sender, EventArgs args)
    {
        await Navigation.PopAsync();
    }
}