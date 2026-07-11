namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Condition picker shown after a pet is created. On Continue it saves the choice
/// onto the pet and performs the same onboarding handoff CreatePetPage used to do
/// directly: into the tabbed Shell if it exists, else switch the window to the
/// main app.
/// </summary>
public partial class ConditionPickerPage : ContentPage
{
    private readonly MainViewModel vm;

    public ConditionPickerPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Highlight whatever is already stored on the just-created pet (defaults
        // to "None / Not sure").
        vm.ConditionVM.Sync();
    }

    private async void OnContinueTapped(object? sender, TappedEventArgs e)
    {
        await vm.ConditionVM.SaveAsync();

        // Rebuild the Calendar's tracking rows so the new condition's items show
        // up immediately when the user lands on the Journal tab.
        await vm.CalendarVM.PrepareDataAsync();

        if (Shell.Current is not null)
        {
            // Added a pet from the Pets tab: land on Today.
            await Shell.Current.GoToAsync("//TodayTab");
        }
        else
        {
            // Finishing first-launch onboarding: hand off to the tabbed app.
            (Application.Current as App)?.SwitchToMainApp();
        }
    }
}
