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

    // Android back closes an open sheet (or the settings panel) before it navigates.
    protected override bool OnBackButtonPressed()
        => Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.ManageVM.RequestConditionSetup += OnRequestConditionSetup;
        vm.ManageVM.RequestEditPet += OnRequestEditPet;
        vm.ManageVM.RequestAddMedication += OnRequestAddMedication;
        vm.ManageVM.RequestOpenMedication += OnRequestOpenMedication;

        // Leaving a shared pet purges it from this device — confirm natively
        // (same accepted exception as delete-account), then pop back since the
        // page's pet is about to vanish.
        vm.SharingVM.ConfirmLeave = () =>
            DisplayAlert(
                Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString("Cloud_LeaveConfirmTitle"),
                Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString("Cloud_LeaveConfirmMessage"),
                Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString("Cloud_Leave"),
                Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString("Common_Cancel"));
        vm.SharingVM.LeftPet += OnLeftPet;

        // Any setup sheet saving should refresh the page's plan + chips.
        vm.DiabetesSetupVM.Saved += OnSheetSaved;
        vm.CkdSetupVM.Saved += OnSheetSaved;
        vm.EpilepsySetupVM.Saved += OnSheetSaved;

        try
        {
            await vm.ManageVM.LoadAsync();
        }
        catch (Exception ex)
        {
            // async void — an escaping exception here would crash the app.
            System.Diagnostics.Debug.WriteLine($"[ManagePetPage] OnAppearing failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        vm.ManageVM.RequestConditionSetup -= OnRequestConditionSetup;
        vm.ManageVM.RequestEditPet -= OnRequestEditPet;
        vm.ManageVM.RequestAddMedication -= OnRequestAddMedication;
        vm.ManageVM.RequestOpenMedication -= OnRequestOpenMedication;

        vm.DiabetesSetupVM.Saved -= OnSheetSaved;
        vm.CkdSetupVM.Saved -= OnSheetSaved;
        vm.EpilepsySetupVM.Saved -= OnSheetSaved;

        vm.SharingVM.ConfirmLeave = null;
        vm.SharingVM.LeftPet -= OnLeftPet;
    }

    private async void OnLeftPet()
    {
        try { await Navigation.PopAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ManagePetPage] pop after leave failed: {ex.Message}"); }
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
