using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        BindingContext = App.Current?.Handler?.MauiContext?.Services.GetService<PetViewModel>() ?? new PetViewModel(new PetDatabase());
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var viewModel = (PetViewModel)BindingContext;
        await viewModel.LoadPetsAsync();
    }

    async void OnCalendarClicked(object sender, EventArgs args)
    {
        await Navigation.PushAsync(new CalendarPage());
    }
}
