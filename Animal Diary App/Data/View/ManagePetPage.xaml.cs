namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;

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
        // Remote changes to this pet's plan/meds landing mid-view reload the page.
        vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;

        // Removing the pet: the page owns the native dialogs (offer the export first,
        // then name the consequence) and the after-removal navigation.
        vm.ManageVM.RequestRemoveFlow = OnRequestRemoveFlow;
        vm.ManageVM.PetRemoved += OnPetRemoved;
        // The export sheet is hosted here too (the "save a copy first" offer opens it);
        // "View" on its done face pushes the preview, exactly as the Pets page does.
        vm.ExportSheetVM.ViewRequested += OnReportViewRequested;

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
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;

        vm.ManageVM.RequestRemoveFlow = null;
        vm.ManageVM.PetRemoved -= OnPetRemoved;
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
    }

    // Runs the native remove dialogs for the active pet and returns what the VM
    // should do. A caregiver leaves (the pet's data stays with the owner); an owner
    // or a not-backed-up pet is deleted, but only after the export is offered first
    // and the consequence is named (see AI/app-voice.md §13).
    private async Task<PetRemovalFlowResult> OnRequestRemoveFlow(PetRemovalKind kind)
    {
        var loc = LocalizationManager.Instance;
        var name = vm.ManageVM.PetName;
        var cancel = loc.GetString("Common_Cancel");

        if (kind == PetRemovalKind.Caregiver)
        {
            var left = await DisplayAlert(
                loc.GetString("Cloud_LeaveConfirmTitle"),
                loc.GetString("Cloud_LeaveConfirmMessage"),
                loc.GetString("Cloud_Leave"),
                cancel);
            return left ? PetRemovalFlowResult.Proceed : PetRemovalFlowResult.Cancel;
        }

        // Offer the export before deleting (people sometimes delete in grief).
        var saveCopy = loc.GetString("Manage_RemovePetSaveCopy");
        var remove = loc.Format("Manage_RemovePetContinue", name);
        var choice = await DisplayActionSheet(
            loc.Format("Manage_RemovePetTitle", name),
            cancel,
            remove,          // destructive
            saveCopy);       // regular option, offered first

        if (choice == saveCopy)
        {
            vm.ExportSheetVM.OpenCommand.Execute(null);
            return PetRemovalFlowResult.SavedCopy;
        }
        if (choice != remove)
            return PetRemovalFlowResult.Cancel;

        // Name the consequence and confirm it's irreversible. A backed-up owner's
        // delete also reaches the cloud and everyone sharing the pet's care.
        var body = kind == PetRemovalKind.OwnerBackedUp
            ? loc.Format("Manage_RemovePetConfirmBodyBackup", name)
            : loc.Format("Manage_RemovePetConfirmBody", name);
        var confirmed = await DisplayAlert(
            loc.Format("Manage_RemovePetConfirmTitle", name),
            body,
            loc.Format("Manage_RemovePetConfirmAccept", name),
            cancel);
        return confirmed ? PetRemovalFlowResult.Proceed : PetRemovalFlowResult.Cancel;
    }

    private async void OnPetRemoved(bool anyPetsRemain)
    {
        try
        {
            if (!anyPetsRemain)
            {
                // The owner deleted their last pet — back to onboarding, as after a
                // full reset (a freshly resolved page, no stale instances).
                Application.Current!.Windows[0].Page = new NavigationPage(new WelcomePage(vm));
                return;
            }

            await vm.LoadAsync();
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ManagePetPage] after-remove nav failed: {ex.Message}");
        }
    }

    /// <summary>The export sheet's "View" — push the in-app preview (navigation
    /// belongs to pages, not VMs). Same handler the Pets page uses.</summary>
    private async void OnReportViewRequested(Data.Models.VetReportFile report)
    {
        try
        {
            vm.ReportPreviewVM.Open(report);
            await Navigation.PushAsync(new ReportPreviewPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ManagePetPage] preview push failed: {ex.Message}");
        }
    }

    private void OnRemoteChangesApplied() =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await vm.ManageVM.LoadAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ManagePetPage] cloud reload failed: {ex}"); }
        });

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
