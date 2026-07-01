namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;

public partial class PetsPage : ContentPage
{
    private CalendarPage? calendarPage;

    private readonly MainViewModel vm;
    public PetsPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        vm.SettingsVM.ResetCompleted += OnResetCompleted;
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

    public async void OnEntryCompleted(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnMainClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new MainPage(vm));
    }

    async void OnCalendarClicked(object? sender, EventArgs args)
    {
        calendarPage ??= new CalendarPage(vm);
        await Navigation.PushAsync(calendarPage);
    }

    async void OnAddPetClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnAddMedicationClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new MedicationsPage(vm));
    }
}
