namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

public partial class CreatePetPage : ContentPage
{
    private MainViewModel vm;

    public CreatePetPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    async void OnBackClicked(object? sender, EventArgs args)
    {
        await Navigation.PopAsync();
    }

    async void OnSaveClicked(object? sender, EventArgs args)
    {
        await vm.PetVM.SavePetAsync();
        await Navigation.PushAsync(new MainPage(vm));
    }
}
