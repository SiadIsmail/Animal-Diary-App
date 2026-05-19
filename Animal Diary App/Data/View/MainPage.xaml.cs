using Animal_Diary_App.Data.ViewModels;
using Microsoft.Extensions.DependencyInjection;
namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;

public partial class MainPage : ContentPage
{
    private MainViewModel vm;
    private CalendarPage? calendarPage;
    private PetsPage? petPage;

    public MainPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.LoadAsync();
    }

    async void OnCalendarClicked(object sender, EventArgs args)
    {
        calendarPage ??= new CalendarPage(vm);
        await Navigation.PushAsync(calendarPage);
    }

    async void OnPetsClicked(object sender, EventArgs args)
    {
        petPage ??= new PetsPage(vm);
        await Navigation.PushAsync(petPage);
    }
}
