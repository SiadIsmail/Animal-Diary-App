namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;

public partial class PetsPage : ContentPage
{
    /// <summary>The overlay sheets are queued for background building once, after the
    /// first load settles. Re-queuing on every remote-change reload would only enqueue
    /// no-ops (Realise is idempotent), but the flag keeps the intent obvious.</summary>
    private bool _sheetsQueued;

    private readonly MainViewModel vm;
    public PetsPage(MainViewModel mainViewModel)
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

        // Only the visible tab animates its backdrop — see MainPage for why.
        Backdrop.Start();

        vm.DevVM.ImportRequested += OnImportRequested;

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // Signed-in reset is a choice: keep the backup or destroy it too. Three outcomes,
        // so it uses the confirm sheet rather than the native action sheet.
        vm.SettingsVM.ConfirmDeleteAllDataCloud = () => ResetScopePrompt.AskAsync(vm.ConfirmVM);

        vm.CloudVM.ConfirmDeleteAccount = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // This page hosts the export sheet, so sign-out can offer "save a copy first".
        vm.CloudVM.ConfirmSignOut = impact =>
            SignOutPrompt.AskAsync(this, impact, vm.ConfirmVM, () => vm.ExportSheetVM.OpenCommand.Execute(null));
        vm.CloudVM.SignedOut += OnSignedOut;

        vm.SettingsVM.ResetCompleted += OnResetCompleted;
        vm.ExportSheetVM.ViewRequested += OnReportViewRequested;
        // Another caregiver's changes landing while this page is visible reload
        // it in place (e.g. a newly shared pet appearing in the list).
        vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;

        // The sharing sheet is hosted here too now (the "Shared care" row). A caregiver
        // leaving purges the pet from this device — confirm natively (same accepted
        // exception as delete-account), then reload the list once it's gone.
        vm.SharingVM.ConfirmLeave = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Cloud_LeaveConfirmTitle"),
                LocalizationManager.Instance.GetString("Cloud_LeaveConfirmMessage"),
                LocalizationManager.Instance.GetString("Cloud_Leave"),
                LocalizationManager.Instance.GetString("Common_Cancel"));
        vm.SharingVM.LeftPet += OnLeftPet;

        try
        {
            // Re-read on every appearance so conditions or medications changed on
            // Manage / Medications are reflected the moment this page returns.
            await vm.PetVM.LoadActivePetTagsAsync();

            // The sheets can be built now that the page has its data: deferred to here
            // deliberately, so their inflation never competes with the load the person
            // is actually waiting for. See Controls/SheetHost.cs.
            if (!_sheetsQueued)
            {
                _sheetsQueued = true;
                Controls.SheetHost.PreloadAll(this);
            }
        }
        catch (Exception ex)
        {
            // A failed load must degrade to a card with no chips, never crash the app
            // (async void — an escaping exception here kills the process).
            System.Diagnostics.Debug.WriteLine($"[PetsPage] OnAppearing failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Backdrop.Stop();
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ConfirmDeleteAllDataCloud = null;
        vm.CloudVM.ConfirmDeleteAccount = null;
        vm.CloudVM.ConfirmSignOut = null;
        vm.CloudVM.SignedOut -= OnSignedOut;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
        vm.SharingVM.ConfirmLeave = null;
        vm.SharingVM.LeftPet -= OnLeftPet;
        vm.DevVM.ImportRequested -= OnImportRequested;
    }

    // Shared care is a first-class action here. Sharing rides the cloud sync (the invite
    // carries the pet's data), so it needs an account + backup — when that isn't set up
    // yet, open the account door (the same one the Welcome screen uses) rather than a
    // sheet that can only say "not synced yet".
    void OnShareClicked(object? sender, EventArgs args)
    {
        if (vm.SharingVM.IsSharingAvailable)
            vm.SharingVM.OpenCommand.Execute(null);
        else
            vm.CloudVM.OpenCommand.Execute(null);
    }

    // Join a shared pet with an invite code. CloudVM.OpenJoinAsync owns the routing:
    // signed in + backup on → the focused code input; otherwise the account door first
    // (joining needs sync on to pull the shared pet), landing on the code input after.
    void OnJoinPetClicked(object? sender, EventArgs args)
    {
        vm.CloudVM.OpenJoinCommand.Execute(null);
    }

    // A caregiver left the pet from the sharing sheet (which closed itself). The pet is
    // being purged from this device by the membership-diff sync that follows the leave;
    // reload now, and RemoteChangesApplied reloads again if the purge lands later.
    private async void OnLeftPet()
    {
        try
        {
            await vm.LoadAsync();
            await vm.PetVM.LoadActivePetTagsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PetsPage] reload after leave failed: {ex}");
        }
    }

    // Signing out removed this account's pets from the device. If nothing is left, the app
    // has nothing to show — route to onboarding exactly as deleting the last pet does.
    // Otherwise reload in place; local-only pets can still be here.
    private async void OnSignedOut(bool anyPetsRemain)
    {
        try
        {
            if (!anyPetsRemain)
            {
                (Application.Current as App)?.SwitchToOnboarding();
                return;
            }
            await vm.LoadAsync();
            await vm.PetVM.LoadActivePetTagsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cloud] after-sign-out refresh failed: {ex.Message}");
        }
    }

    private void OnRemoteChangesApplied() =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // The pet list itself may have changed (joined/purged pets), not
                // just the active pet's chips — reload both.
                await vm.LoadAsync();
                await vm.PetVM.LoadActivePetTagsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PetsPage] cloud reload failed: {ex}");
            }
        });

    /// <summary>The export sheet's "View" button — push the in-app preview for the
    /// report it just generated (navigation belongs to pages, not VMs).</summary>
    private async void OnReportViewRequested(Data.Models.VetReportFile report)
    {
        try
        {
            vm.ReportPreviewVM.Open(report);
            await Navigation.PushAsync(new ReportPreviewPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] preview push failed: {ex}");
        }
    }

    private void OnResetCompleted(object? sender, EventArgs e)
    {
        // Clear in-memory form drafts so stale inputs don't survive the wipe
        // (the ViewModels are singletons).
        vm.ResetDrafts();
        // Through App, not Windows[0]: it records the "no pets" state so an Activity
        // recreation later rebuilds onboarding rather than the Shell, and it targets
        // the window actually on screen.
        (Application.Current as App)?.SwitchToOnboarding();
    }

    public async void OnEntryCompleted(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnAddPetClicked(object? sender, EventArgs args)
    {
        // Adding another pet is "adding more" — gated in the read-only state. The first
        // pet is created during onboarding (a different path), so it is never gated here.
        if (!vm.Entitlements.HasFullAccess)
        {
            vm.SubscribeVM.Open(Animal_Diary_App.Data.Services.Analytics.AnalyticsEvents.SubscribeSourceReadOnly);
            return;
        }
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnManageClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new ManagePetPage(vm));
    }

    // The "Medication" button on the active pet's card. Named for the page it opens
    // (the medication list for this pet), not for adding — adding is one of the things
    // that page does, and it was the old row's misleading name.
    async void OnMedicationClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new MedicationsPage(vm));
    }

    async void OnDocumentsClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new DocumentsPage(vm));
    }

    // The appointment page reads what is already there; adding a visit from it goes
    // through the visit sheet, which is where any gate would belong. Not gated here,
    // like the Constellation below.
    async void OnVetVisitsClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new AppointmentPage(vm));
    }

    // The Constellation reads what is already there and writes nothing, so it is not
    // gated by the entitlement — the read-only state keeps records readable.
    async void OnConstellationClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new ConstellationPage(vm));
    }

    // The dev sheet is a ContentView and cannot navigate, so the hosting page pushes the
    // importer on its behalf (same shape as the export sheet's ViewRequested below).
    private async void OnImportRequested()
    {
        try
        {
            await Navigation.PushAsync(new ImportPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Import] push failed: {ex}");
        }
    }

}
