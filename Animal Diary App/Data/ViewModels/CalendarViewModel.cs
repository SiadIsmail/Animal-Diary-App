namespace Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Models.Commands;
using System.ComponentModel;
using System.Runtime.CompilerServices;
public class CalendarViewModel : INotifyPropertyChanged
{
        public event PropertyChangedEventHandler PropertyChanged;
	public string enteredMood { get; set; } = string.Empty;

    public string EnteredMood
    {
        get => enteredMood;
        set
        {
            if (enteredMood == value)
            {
                return;
            }

            enteredMood = value;
            OnPropertyChanged();
        }
    }
    
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}