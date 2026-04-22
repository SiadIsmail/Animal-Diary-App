namespace Animal_Diary_App.Data.View;
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
		await Shell.Current.GoToAsync(nameof(PetTypePage));
	}
}
