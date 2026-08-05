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

    // Crop/rotate the photo that is already on the draft — no picker involved. The way
    // back into the editor for a photo that turned out crooked, which on some devices is
    // still how camera shots arrive (see PetPhotoService's "[PetPhoto]" logging).
    async void OnAdjustPhoto(object? sender, EventArgs args)
    {
        try
        {
            var source = vm.PetVM.DescribeDraftPhoto();
            if (source is null)
            {
                // The file is gone or unreadable; the chooser stays open to say so.
                ShowPhotoError("CreatePet_PhotoError");
                return;
            }

            PhotoChooser.IsPresented = false;
            await RunEditorAsync(source);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CreatePet] photo edit failed: {ex}");
            ShowPhotoError("CreatePet_PhotoError");
        }
    }

    // Shared pick/capture path: run the media action, stage the result for the editor,
    // and let the owner square it up before it becomes the draft. A cancelled pick
    // returns null (no-op); a denied permission or any failure degrades to an inline
    // message, never a crash — this is an async void handler, so an escaping exception
    // would kill the process.
    private async Task PickAsync(Func<Task<FileResult?>> pick)
    {
        try
        {
            var result = await pick();
            if (result is null)
                return; // user cancelled

            Models.PhotoEditSource source;
            try
            {
                using var stream = await result.OpenReadAsync();
                source = await vm.PetVM.PrepareDraftPhotoAsync(stream);
            }
            catch (Exception ex)
            {
                // Staging is the one step that can fail without costing the owner their
                // photo: fall back to saving it exactly as this flow did before the
                // editor existed. A photo we can't crop is still a photo they picked.
                Debug.WriteLine($"[CreatePet] photo staging failed, saving unedited: {ex}");
                using var stream = await result.OpenReadAsync();
                await vm.PetVM.SetDraftPhotoAsync(stream);
                PhotoChooser.IsPresented = false;
                return;
            }

            PhotoChooser.IsPresented = false;
            await RunEditorAsync(source);
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

    // Hand a staged photo to the editor sheet and wait. Null back means the owner backed
    // out, and then nothing about their photo changes — that is the whole contract, and
    // it is why the draft isn't touched until this returns.
    private async Task RunEditorAsync(Models.PhotoEditSource source)
    {
        var transform = await vm.PhotoEditorVM.EditAsync(source);

        if (transform is null)
        {
            vm.PetVM.DiscardPhotoEdit(source);
            return;
        }

        await vm.PetVM.ApplyDraftPhotoAsync(source, transform.Value);
    }

    private void ShowPhotoError(string key)
    {
        PhotoErrorLabel.Text = LocalizationManager.Instance.GetString(key);
        PhotoErrorLabel.IsVisible = true;

        // This line lives inside the chooser, so bring the chooser back if the failure
        // happened after it closed — otherwise the message would be shown to no one.
        PhotoChooser.IsPresented = true;
    }
}
