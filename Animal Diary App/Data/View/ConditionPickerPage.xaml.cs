namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Analytics;

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

    // Android back closes an open sheet (or the settings panel) before it navigates.
    protected override bool OnBackButtonPressed()
        => Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // The reusable setup sheets are the SAME ones the Manage page uses; opening +
        // refreshing is coordinated exactly the same way.
        vm.ConditionVM.RequestConditionSetup += OnRequestConditionSetup;
        vm.DiabetesSetupVM.Saved += OnSheetSaved;
        vm.CkdSetupVM.Saved += OnSheetSaved;
        vm.EpilepsySetupVM.Saved += OnSheetSaved;

        // Reflect whatever is already stored on the just-created pet.
        await vm.ConditionVM.SyncAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        vm.ConditionVM.RequestConditionSetup -= OnRequestConditionSetup;
        vm.DiabetesSetupVM.Saved -= OnSheetSaved;
        vm.CkdSetupVM.Saved -= OnSheetSaved;
        vm.EpilepsySetupVM.Saved -= OnSheetSaved;
    }

    private async void OnRequestConditionSetup(string conditionId)
    {
        switch (conditionId)
        {
            case "diabetes": await vm.DiabetesSetupVM.OpenAsync(); break;
            case "ckd": await vm.CkdSetupVM.OpenAsync(); break;
            case "epilepsy": await vm.EpilepsySetupVM.OpenAsync(); break;
        }
    }

    private async void OnSheetSaved() => await vm.ConditionVM.SyncAsync();

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
            // Finishing first-launch onboarding: this branch runs only when there is no
            // Shell yet (adding a pet from the Pets tab takes the branch above), so it
            // marks the true end of the onboarding funnel.

            // Did the user actually set up a condition, or continue past it? The row
            // states are current (SyncAsync ran on appear and after every sheet save).
            // We record only whether setup happened — never which condition.
            var configuredCondition = vm.ConditionVM.Conditions.Any(c => c.IsSelected && !c.IsNone);
            vm.Analytics.Track(configuredCondition
                ? AnalyticsEvents.ConditionSetupCompleted
                : AnalyticsEvents.ConditionSetupSkipped);

            // Now that the pet exists, offer backup + sharing once at this concrete-value
            // moment — unless the owner already turned backup on (e.g. via the Welcome
            // account door), in which case there's nothing to offer and we go straight in.
            // KeepSafePage fires OnboardingCompleted at its own handoff, so the funnel
            // still closes exactly once, on entering the app.
            if (vm.CloudSync.IsBackupEnabled)
            {
                vm.Analytics.Track(AnalyticsEvents.OnboardingCompleted);
                (Application.Current as App)?.SwitchToMainApp();
            }
            else
            {
                await Navigation.PushAsync(new KeepSafePage(vm));
            }
        }
    }
}
