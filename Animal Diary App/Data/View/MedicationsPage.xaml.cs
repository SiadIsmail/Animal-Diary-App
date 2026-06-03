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
        await vm.PetVM.LoadPetsAsync();
        await vm.MedicationVM.LoadFilteredMedicationAsync();
    }
    async void OnBackClicked(object? sender, EventArgs args)
    {
        await Navigation.PopAsync();
    }

    async void OnAddMedicationClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new AddEditMedicationsPage(vm));
    }
}