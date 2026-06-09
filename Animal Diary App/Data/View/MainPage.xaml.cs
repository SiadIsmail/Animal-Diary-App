using Animal_Diary_App.Data.ViewModels;
namespace Animal_Diary_App.Data.View;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel vm;
    private CalendarPage? calendarPage;
    private PetsPage? petPage;

    public MainPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                "Delete All Data",
                "This will permanently delete all pets, logs, medications, and settings. This cannot be undone.",
                "Delete",
                "Cancel");

        vm.SettingsVM.ResetCompleted += OnResetCompleted;

        await vm.LoadAsync();
        await vm.MainPageVM.LoadWeightChartAsync();
        await vm.MainPageVM.LoadMoodTimelineAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
    }

    private void OnResetCompleted(object? sender, EventArgs e)
    {
        Application.Current!.Windows[0].Page = new NavigationPage(new WelcomePage(vm));
    }

    async void OnCalendarClicked(object? sender, EventArgs args)
    {
        calendarPage ??= new CalendarPage(vm);
        await Navigation.PushAsync(calendarPage);
    }

    async void OnPetsClicked(object? sender, EventArgs args)
    {
        petPage ??= new PetsPage(vm);
        await Navigation.PushAsync(petPage);
    }
}
