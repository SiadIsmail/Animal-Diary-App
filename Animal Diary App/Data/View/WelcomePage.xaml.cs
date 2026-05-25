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

	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await Navigation.PushAsync(new CreatePetPage(vm));
	}
}
