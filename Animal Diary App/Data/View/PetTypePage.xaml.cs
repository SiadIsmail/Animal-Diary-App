namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;

public partial class PetTypePage : ContentPage
{


	private MainViewModel vm;
	public PetTypePage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await vm.PetVM.SavePetAsync(); //Refactor this to be in the view model, not the view -R
		await Navigation.PushAsync(new MainPage(vm));
	}




}
