using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;

public partial class MainPage : ContentPage
{
    private MainViewModel vm;
    public MainPage()
    {
        InitializeComponent();
        vm = new MainViewModel();

        BindingContext = vm;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.LoadAsync();
    }

    async void OnCalendarClicked(object sender, EventArgs args)
    {
        await Navigation.PushAsync(new CalendarPage());
    }
}
