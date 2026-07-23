namespace Animal_Diary_App.Data.View;

using System.Diagnostics;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Media;

public partial class CreatePetPage : ContentPage
{
    private MainViewModel vm;
    private readonly bool isEditMode;

    // Onboarding step 1 configures the draft exactly once. Coming BACK here from the
    // details page (step 2) must not reset the draft or it would wipe the name and
    // details the user already entered.
    private bool _initialized;

    public CreatePetPage(MainViewModel mainViewModel, bool isEditMode = false)
    {
        InitializeComponent();
        vm = mainViewModel;
        this.isEditMode = isEditMode;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
            return;
        _initialized = true;

        // Edit mode keeps the draft the caller (Manage page) loaded for the pet being
        // edited, and shows the "You're editing X" title. Add/first-launch mode opens on
        // a clean draft so stale inputs from a previous creation don't reappear.
        if (isEditMode)
        {
            vm.PetVM.ConfigureForEdit();
            return;
        }

        // The add/first-launch pet form was opened. Sits between onboarding_started and
        // pet_created so form abandonment (opened the form, never saved) is visible.
        vm.Analytics.Track(AnalyticsEvents.PetFormStarted);

        vm.PetVM.ResetDraft();
        await vm.PetVM.CheckAndSetFirstLaunchAsync();
    }

    // Android back closes the photo chooser before it navigates.
    protected override bool OnBackButtonPressed()
        => Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    async void OnCancelClicked(object? sender, EventArgs args)
    {
        // Cancel discards the in-progress draft.
        vm.PetVM.ResetDraft();
        await Navigation.PopAsync();
    }

    async void OnNextClicked(object? sender, EventArgs args)
    {
        // Carry the shared draft to step 2 (technical details). The draft lives on the
        // singleton PetVM, so nothing needs to be passed explicitly.
        await Navigation.PushAsync(new PetDetailsPage(vm, isEditMode));
    }

    // ── Profile photo ────────────────────────────────────────────────────────────

    void OnPhotoTapped(object? sender, EventArgs args)
    {
        PhotoErrorLabel.IsVisible = false;
        PhotoChooser.IsPresented = true;
    }

    async void OnTakePhoto(object? sender, EventArgs args)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ShowPhotoError("CreatePet_PhotoCaptureUnsupported");
            return;
        }

        await PickAsync(() => MediaPicker.Default.CapturePhotoAsync());
    }

    async void OnChooseFromLibrary(object? sender, EventArgs args)
        => await PickAsync(() => MediaPicker.Default.PickPhotoAsync());

    void OnRemovePhoto(object? sender, EventArgs args)
    {
        vm.PetVM.ClearDraftPhoto();
        PhotoChooser.IsPresented = false;
    }

    // Shared pick/capture path: run the media action, copy the result into app storage
    // via the VM, and close the sheet. A cancelled pick returns null (no-op); a denied
    // permission or any failure degrades to an inline message, never a crash — this is
    // an async void handler, so an escaping exception would kill the process.
    private async Task PickAsync(Func<Task<FileResult?>> pick)
    {
        try
        {
            var result = await pick();
            if (result is null)
                return; // user cancelled

            using var stream = await result.OpenReadAsync();
            await vm.PetVM.SetDraftPhotoAsync(stream);
            PhotoChooser.IsPresented = false;
        }
        catch (PermissionException)
        {
            ShowPhotoError("CreatePet_PhotoPermissionDenied");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CreatePet] photo pick failed: {ex}");
            ShowPhotoError("CreatePet_PhotoError");
        }
    }

    private void ShowPhotoError(string key)
    {
        PhotoErrorLabel.Text = LocalizationManager.Instance.GetString(key);
        PhotoErrorLabel.IsVisible = true;
    }
}
