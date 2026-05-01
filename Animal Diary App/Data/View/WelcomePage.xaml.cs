namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;

public partial class WelcomePage : ContentPage
{

	public WelcomePage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel(new PetDatabase());
	}

	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		await Shell.Current.GoToAsync(nameof(PetAgePage));
	}
}
