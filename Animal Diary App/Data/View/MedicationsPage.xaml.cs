using Animal_Diary_App.Data.ViewModels;
namespace Animal_Diary_App.Data.View;

public partial class MedicationsPage : ContentPage
{
    private MainViewModel vm;

    public MedicationsPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await vm.PetVM.LoadPetsAsync();
            await vm.MedicationVM.LoadFilteredMedicationAsync();
        }
        catch (Exception ex)
        {
            // async void — an escaping exception here would crash the app.
            System.Diagnostics.Debug.WriteLine($"[MedicationsPage] OnAppearing failed: {ex}");
        }
    }
    async void OnBackClicked(object? sender, EventArgs args)
    {
        await Navigation.PopAsync();
    }
}