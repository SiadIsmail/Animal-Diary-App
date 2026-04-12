namespace Animal_Diary_App.Data;

using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;

public partial class WelcomePage : ContentPage
{

	public WelcomePage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();
	}

	private async void OnEntryCompleted(object? sender, EventArgs e)
	{
		if (BindingContext is PetViewModel petViewModel)
		{
			petViewModel.EnteredPetName = entry.Text ?? string.Empty;
		}

		await Shell.Current.GoToAsync(nameof(PetAgePage));
	}
}
