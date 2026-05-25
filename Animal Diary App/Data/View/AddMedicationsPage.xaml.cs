using Animal_Diary_App.Data.ViewModels;
namespace Animal_Diary_App.Data.View;

public partial class AddEditMedicationsPage : ContentPage
{
    private MainViewModel vm;
    private MedicationViewModel medicationVM;

    public AddEditMedicationsPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        medicationVM = vm.MedicationVM;
        BindingContext = vm;

        medicationVM.OnMedicationSaved += async (s, e) =>
        {
            await Navigation.PopAsync();
        };
    }

    async void OnBackClicked(object? sender, EventArgs args)
    {
        await Navigation.PopAsync();
    }
}
