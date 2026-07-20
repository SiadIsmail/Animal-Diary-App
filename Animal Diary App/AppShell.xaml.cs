namespace Animal_Diary_App;

using Animal_Diary_App.Data.View;

public partial class AppShell : Shell
{
    // The tab pages are injected (they take MainViewModel via constructor DI) and
    // assigned to the ShellContent slots once. Shell keeps these instances alive
    // for the Shell's lifetime, so switching tabs never rebuilds them.
    public AppShell(MainPage today, CalendarPage journal, PetsPage pets)
    {
        InitializeComponent();

        TodayTab.Content = today;
        JournalTab.Content = journal;
        PetsTab.Content = pets;
    }
}
