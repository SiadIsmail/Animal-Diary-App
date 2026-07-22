namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

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

        vm.PetVM.ResetDraft();
        await vm.PetVM.CheckAndSetFirstLaunchAsync();
    }

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
}
