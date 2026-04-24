namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class PetTypePage : ContentPage
{


	public PetTypePage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();
	}

	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync(nameof(MainPage));
	}




}
