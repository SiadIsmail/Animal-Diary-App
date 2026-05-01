namespace Animal_Diary_App.Data.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class MainPageViewModel : INotifyPropertyChanged
{
    private readonly PetViewModel petViewModel;
    private readonly CalendarViewModel calendarViewModel;

    public event PropertyChangedEventHandler? PropertyChanged;

   

   

    


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
