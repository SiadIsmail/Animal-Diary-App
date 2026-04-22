namespace Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Models.Commands;
using System.ComponentModel;
using System.Runtime.CompilerServices;
public class CalendarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private string enteredMood = string.Empty;

    public string EnteredMood
    {
        get => enteredMood;
        set
        {
            if (enteredMood == value) return;
            enteredMood = value;
            OnPropertyChanged();
        }
    }

  
    public List<PetDiaryEntry> Entries { get; set; } = new();

    public void SaveEntry()
    {
        var entry = new PetDiaryEntry
        {
            Date = DateTime.Now,
            Mood = EnteredMood
        };

        Entries.Add(entry);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}