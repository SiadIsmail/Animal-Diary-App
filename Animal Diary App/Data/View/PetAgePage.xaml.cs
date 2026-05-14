namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services;

using Microsoft.Extensions.DependencyInjection;
using Windows.Networking.Vpn;

public partial class PetAgePage : ContentPage
{

	private MainViewModel vm;
	public PetAgePage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	public async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await Navigation.PushAsync(new PetTypePage(vm));
	}
}
