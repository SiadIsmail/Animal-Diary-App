namespace Animal_Diary_App.Data.View.Controls;

/// <summary>
/// The floating three-tab bar shared by the Today, Journal and Pets pages.
/// Pure chrome: it renders the tabs, highlights the one named by
/// <see cref="ActiveTab"/> ("Main" | "Calendar" | "Pets"), and routes taps on
/// the other two through the Shell tab routes. Tapping the active tab is a no-op.
/// </summary>
public partial class BottomNavigation : ContentView
{
    public BottomNavigation()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(BottomNavigation), defaultValue: "Main");

    /// <summary>Which tab to highlight: "Main", "Calendar" or "Pets".</summary>
    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    private async void OnMainClicked(object? sender, EventArgs args)
    {
        if (ActiveTab != "Main")
            await Shell.Current.GoToAsync("//TodayTab");
    }

    private async void OnCalendarClicked(object? sender, EventArgs args)
    {
        if (ActiveTab != "Calendar")
            await Shell.Current.GoToAsync("//JournalTab");
    }

    private async void OnPetsClicked(object? sender, EventArgs args)
    {
        if (ActiveTab != "Pets")
            await Shell.Current.GoToAsync("//PetsTab");
    }
}
