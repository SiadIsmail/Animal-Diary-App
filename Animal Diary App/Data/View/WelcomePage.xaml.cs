namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;

public partial class WelcomePage : ContentPage
{

	private MainViewModel vm;
	public WelcomePage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	async void OnAddPetClicked(object? sender, EventArgs args)
	{
		await Navigation.PushAsync(new CreatePetPage(vm));
	}
}
