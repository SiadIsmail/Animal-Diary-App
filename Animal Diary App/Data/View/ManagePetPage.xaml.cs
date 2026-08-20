namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
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

    // Feature-discovery counts "opened Manage" once per navigation into the page — the
    // guard stops OnAppearing re-firing it when returning from the edit-pet or
    // medications sub-pages (a new page instance is created per entry from the Care tab).
    private bool _openTracked;

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

        if (!_openTracked)
        {
            _openTracked = true;
            vm.Analytics.Track(AnalyticsEvents.ManagePetOpened);
        }

        vm.ManageVM.RequestConditionSetup += OnRequestConditionSetup;
        vm.ManageVM.RequestTrackerSetup += OnRequestTrackerSetup;
        vm.ManageVM.RequestCustomTracker += OnRequestCustomTracker;
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
        // Pausing a pet quietly offers the export once (§15.5) — the page owns the offer.
        vm.ManageVM.RequestPauseExportOffer += OnPauseExportOffer;
        // The export sheet is hosted here too (the "save a copy first" offer opens it);
        // "View" on its done face pushes the preview, exactly as the Pets page does.
        vm.ExportSheetVM.ViewRequested += OnReportViewRequested;

        // Any setup sheet saving should refresh the page's plan + chips.
        vm.DiabetesSetupVM.Saved += OnSheetSaved;
        vm.CkdSetupVM.Saved += OnSheetSaved;
        vm.EpilepsySetupVM.Saved += OnSheetSaved;
        vm.CustomTrackerVM.Changed += OnSheetSaved;
        // Retiring an owner-defined tracker has no undo, so it asks first. The
        // message leads with what is KEPT — nothing written down is affected.
        vm.CustomTrackerVM.ConfirmRetire = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Custom_RetireConfirmTitle"),
                LocalizationManager.Instance.GetString("Custom_RetireConfirmBody"),
                LocalizationManager.Instance.GetString("Custom_Retire"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

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
        vm.ManageVM.RequestTrackerSetup -= OnRequestTrackerSetup;
        vm.ManageVM.RequestCustomTracker -= OnRequestCustomTracker;
        vm.ManageVM.RequestEditPet -= OnRequestEditPet;
        vm.ManageVM.RequestAddMedication -= OnRequestAddMedication;
        vm.ManageVM.RequestOpenMedication -= OnRequestOpenMedication;

        vm.DiabetesSetupVM.Saved -= OnSheetSaved;
        vm.CkdSetupVM.Saved -= OnSheetSaved;
        vm.EpilepsySetupVM.Saved -= OnSheetSaved;
        vm.CustomTrackerVM.Changed -= OnSheetSaved;
        vm.CustomTrackerVM.ConfirmRetire = null;

        vm.SharingVM.ConfirmLeave = null;
        vm.SharingVM.LeftPet -= OnLeftPet;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;

        vm.ManageVM.RequestRemoveFlow = null;
        vm.ManageVM.PetRemoved -= OnPetRemoved;
        vm.ManageVM.RequestPauseExportOffer -= OnPauseExportOffer;
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
    }

    // After a pet is paused, quietly offer the export once (AI/app-voice.md §15.5).
    // Reversible and non-destructive, so it's a gentle offer, not a confirmation.
    private async void OnPauseExportOffer()
    {
        try
        {
            var loc = LocalizationManager.Instance;
            var name = vm.ManageVM.PetName;
            var saveCopy = await DisplayAlert(
                loc.Format("Manage_PausedExportTitle", name),
                loc.Format("Manage_PausedExportBody", name),
                loc.GetString("Manage_PausedExportSave"),
                loc.GetString("Common_NotNow"));
            if (saveCopy)
                vm.ExportSheetVM.OpenCommand.Execute(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ManagePetPage] pause export offer failed: {ex.Message}");
        }
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

        // Offer the export before deleting (people sometimes delete in grief). Three
        // outcomes, so it uses the confirm sheet: the native action sheet rendered this
        // offer as plain text under a styled "Remove", which is the wrong emphasis for
        // the one option that protects the owner.
        const string saveCopyId = "save-copy";
        const string removeId = "remove";
        var choice = await vm.ConfirmVM.AskAsync(
            loc.Format("Manage_RemovePetTitle", name),
            body: null,
            new[]
            {
                new ConfirmOption
                {
                    Id = saveCopyId,
                    Label = loc.GetString("Manage_RemovePetSaveCopy"),
                },
                new ConfirmOption
                {
                    Id = removeId,
                    Label = loc.Format("Manage_RemovePetContinue", name),
                    IsDestructive = true,
                },
            });

        if (choice == saveCopyId)
        {
            vm.ExportSheetVM.OpenCommand.Execute(null);
            return PetRemovalFlowResult.SavedCopy;
        }
        if (choice != removeId)
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
                // full reset (a freshly resolved page, no stale instances). Through App
                // so the "no pets" state sticks for any window a recreation builds.
                (Application.Current as App)?.SwitchToOnboarding();
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

    /// <summary>A tracker's own editor, reached by tapping its care-plan row. Reuses the
    /// condition sheets because they already hold the right controls, but with
    /// linkCondition:false — editing a glucose range must never record that the pet has
    /// diabetes, and a seizure log must never claim epilepsy.</summary>
    private async void OnRequestTrackerSetup(Data.Models.TrackerId trackerId)
    {
        switch (trackerId)
        {
            case Data.Models.TrackerId.Glucose: await vm.DiabetesSetupVM.OpenAsync(linkCondition: false); break;
            case Data.Models.TrackerId.Seizure: await vm.EpilepsySetupVM.OpenAsync(linkCondition: false); break;
        }
    }

    // The owner's own trackers: one sheet, opened empty to create or on a row to edit.
    private void OnRequestCustomTracker(Data.Models.CustomTracker? tracker)
    {
        if (tracker is null)
            vm.CustomTrackerVM.OpenNew();
        else
            vm.CustomTrackerVM.OpenEdit(tracker);
    }

    // ── The paywall NEVER appears on this page's add/edit actions. ────────────────
    // The care plan, the trackers, the conditions and the pet profile are all things the
    // owner writes down, and writing things down is free forever on every tier. This page
    // used to route each of them to the subscribe sheet; that gate is exactly what the
    // monetization boundary was inverted to remove. The genuinely owner-only actions here
    // — minting invites, removing members, deleting the pet cloud-wide — are enforced
    // server-side by role, and minting an invite carries its own (unchanged) gate inside
    // SharingSheetViewModel.

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
