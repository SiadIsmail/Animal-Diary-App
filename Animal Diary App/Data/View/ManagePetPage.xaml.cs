namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// The Manage Pet page (see the XAML). Reached from the Care tab's single "Manage"
/// button. Coordinates navigation (edit-pet door, medications) and opens the reusable
/// condition setup sheets on the ManageVM's request, refreshing when any sheet saves.
/// </summary>
public partial class ManagePetPage : ContentPage
{
    private readonly MainViewModel vm;

    public ManagePetPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.ManageVM.RequestConditionSetup += OnRequestConditionSetup;
        vm.ManageVM.RequestEditPet += OnRequestEditPet;
        vm.ManageVM.RequestAddMedication += OnRequestAddMedication;
        vm.ManageVM.RequestOpenMedication += OnRequestOpenMedication;
        vm.ManageVM.RequestOpenAddConditionSheet += OnRequestOpenAddConditionSheet;

        // Any setup sheet saving should refresh the page's plan + chips.
        vm.DiabetesSetupVM.Saved += OnSheetSaved;
        vm.CkdSetupVM.Saved += OnSheetSaved;
        vm.EpilepsySetupVM.Saved += OnSheetSaved;

        await vm.ManageVM.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        vm.ManageVM.RequestConditionSetup -= OnRequestConditionSetup;
        vm.ManageVM.RequestEditPet -= OnRequestEditPet;
        vm.ManageVM.RequestAddMedication -= OnRequestAddMedication;
        vm.ManageVM.RequestOpenMedication -= OnRequestOpenMedication;
        vm.ManageVM.RequestOpenAddConditionSheet -= OnRequestOpenAddConditionSheet;

        vm.DiabetesSetupVM.Saved -= OnSheetSaved;
        vm.CkdSetupVM.Saved -= OnSheetSaved;
        vm.EpilepsySetupVM.Saved -= OnSheetSaved;
    }

    private async void OnRequestConditionSetup(string conditionId)
    {
        switch (conditionId)
        {
            case "diabetes": await vm.DiabetesSetupVM.OpenAsync(); break;
            case "ckd": await vm.CkdSetupVM.OpenAsync(); break;
            case "epilepsy": await vm.EpilepsySetupVM.OpenAsync(); break;
        }
    }

    private async void OnSheetSaved() => await vm.ManageVM.LoadAsync();

    // Diagnostic: force the add-condition BindableLayout to rebuild its children
    // immediately before the sheet opens.
    private void OnRequestOpenAddConditionSheet()
    {
        AddConditionSheet.RefreshItems();
        vm.ManageVM.IsAddConditionSheetVisible = true;
    }

    // Edit-pet door: prefill the create form from the active pet, then reuse it in edit
    // mode (it saves in place and pops back — no condition picker).
    private async void OnRequestEditPet()
    {
        vm.PetVM.LoadDraftFromActivePet();
        await Navigation.PushAsync(new CreatePetPage(vm, isEditMode: true));
    }

    private async void OnRequestAddMedication() =>
        await Navigation.PushAsync(new MedicationsPage(vm));

    private async void OnRequestOpenMedication(int medicationId) =>
        await Navigation.PushAsync(new MedicationsPage(vm));

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync();
}
