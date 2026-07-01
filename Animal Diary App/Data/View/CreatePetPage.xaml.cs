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

        // Add mode always opens on a clean draft so stale inputs from a previous
        // creation don't reappear. Edit mode keeps the draft the caller loaded
        // for the pet being edited.
        if (!isEditMode)
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
        await vm.PetVM.SavePetAsync();
        await Navigation.PushAsync(new MainPage(vm));
    }
}
