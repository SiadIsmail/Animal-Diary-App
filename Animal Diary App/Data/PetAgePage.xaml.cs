namespace Animal_Diary_App.Data;
using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
public partial class PetAgePage : ContentPage
{
	

	public PetAgePage()
	{
		InitializeComponent();
		BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel();
	}

	public async void OnEntryCompleted(object? sender, EventArgs e)
	{
		if (BindingContext is PetViewModel petViewModel)
		{
			petViewModel.EnteredPetAge = entry.Text ?? string.Empty;
		}

		await Shell.Current.GoToAsync(nameof(PetTypePage));
	}
}
