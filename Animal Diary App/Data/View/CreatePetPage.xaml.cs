namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

public partial class CreatePetPage : ContentPage
{
    private MainViewModel vm;
    private readonly bool isEditMode;

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

        // Edit mode keeps the draft the caller (Manage page) loaded for the pet being
        // edited, and shows the "You're editing X" title. Add mode opens on a clean
        // draft so stale inputs from a previous creation don't reappear.
        if (isEditMode)
        {
            vm.PetVM.ConfigureForEdit();
            return;
        }

        vm.PetVM.ResetDraft();
        await vm.PetVM.CheckAndSetFirstLaunchAsync();
    }

    async void OnBackClicked(object? sender, EventArgs args)
    {
        // Cancel discards the in-progress draft.
        vm.PetVM.ResetDraft();
        await Navigation.PopAsync();
    }

    async void OnSaveClicked(object? sender, EventArgs args)
    {
        if (isEditMode)
        {
            // Edit-pet door: save in place and return to Manage (no condition picker).
            if (await vm.PetVM.SaveEditedPetAsync())
                await Navigation.PopAsync();
            return;
        }

        await vm.PetVM.SavePetAsync();

        // The pet now exists and is the active pet. Ask about its condition next;
        // the picker performs the final handoff into the tabbed app (Shell) itself.
        await Navigation.PushAsync(new ConditionPickerPage(vm));
    }
}
