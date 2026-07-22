namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Onboarding step 2 of 2 (technical details: species + birthday). Pushed from
/// CreatePetPage. It shares the same <see cref="MainViewModel"/> / PetVM draft, so the
/// name entered on step 1 is already present. Saving branches on the mode: the create
/// flow hands off to the condition picker; the edit flow saves in place and returns to
/// the Manage page.
/// </summary>
public partial class PetDetailsPage : ContentPage
{
    private readonly MainViewModel vm;
    private readonly bool isEditMode;

    public PetDetailsPage(MainViewModel mainViewModel, bool isEditMode = false)
    {
        InitializeComponent();
        vm = mainViewModel;
        this.isEditMode = isEditMode;
        BindingContext = vm;
    }

    async void OnBackClicked(object? sender, EventArgs args)
    {
        // Back keeps the draft — the user is stepping back to edit the name, not
        // cancelling. Step 1 does not re-reset the draft (guarded there).
        await Navigation.PopAsync();
    }

    async void OnSaveClicked(object? sender, EventArgs args)
    {
        if (isEditMode)
        {
            // Edit-pet door: save in place and return to the Manage page. The stack is
            // [Manage, CreatePetPage(step 1), PetDetailsPage(this)]. Remove step 1 from
            // underneath, then pop this page so we land straight back on Manage — no
            // condition picker.
            if (await vm.PetVM.SaveEditedPetAsync())
            {
                var stack = Navigation.NavigationStack;
                if (stack.Count >= 2)
                    Navigation.RemovePage(stack[stack.Count - 2]);
                await Navigation.PopAsync();
            }
            return;
        }

        await vm.PetVM.SavePetAsync();

        // The pet now exists and is the active pet. Ask about its condition next; the
        // picker performs the final handoff into the tabbed app (Shell) itself.
        await Navigation.PushAsync(new ConditionPickerPage(vm));
    }
}
