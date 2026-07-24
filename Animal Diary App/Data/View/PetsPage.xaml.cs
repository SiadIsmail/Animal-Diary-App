namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;

public partial class PetsPage : ContentPage
{
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

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // Signed-in reset is a choice: keep the backup or destroy it too.
        vm.SettingsVM.ConfirmDeleteAllDataCloud = async () =>
        {
            var deviceOnly = LocalizationManager.Instance.GetString("Settings_ResetDeviceOnly");
            var everything = LocalizationManager.Instance.GetString("Settings_ResetEverything");
            var choice = await DisplayActionSheet(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Common_Cancel"),
                everything,
                deviceOnly);
            if (choice == deviceOnly) return Data.ViewModels.ResetScope.DeviceOnly;
            if (choice == everything) return Data.ViewModels.ResetScope.Everything;
            return null;
        };

        vm.CloudVM.ConfirmDeleteAccount = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

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
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ConfirmDeleteAllDataCloud = null;
        vm.CloudVM.ConfirmDeleteAccount = null;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
        vm.ExportSheetVM.ViewRequested -= OnReportViewRequested;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
        vm.SharingVM.ConfirmLeave = null;
        vm.SharingVM.LeftPet -= OnLeftPet;
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
        Application.Current!.Windows[0].Page = new NavigationPage(new WelcomePage(vm));
    }

    public async void OnEntryCompleted(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnAddPetClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new CreatePetPage(vm));
    }

    async void OnManageClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new ManagePetPage(vm));
    }

    async void OnAddMedicationClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new MedicationsPage(vm));
    }

    async void OnDocumentsClicked(object? sender, EventArgs args)
    {
        await Navigation.PushAsync(new DocumentsPage(vm));
    }
}
