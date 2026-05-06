namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SQLite;

public class CalendarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private DateTime currentSelectedDate = DateTime.Now.Date;
    public DateTime CurrentSelectedDate
    {
        get => currentSelectedDate;
        set
        {
            currentSelectedDate = value.Date;
            OnPropertyChanged();
            _ = LoadEntriesAsync();

        }
    }

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
    private string shownMood = string.Empty;
    public string ShownMood
    {
        get => shownMood;
        set
        {
            if (shownMood == value) return;
            shownMood = value;
            OnPropertyChanged();
        }
    }


    public List<PetEntry> Entries { get; set; } = new();
    private readonly PetEntryDatabase _database;

    public CalendarViewModel(PetEntryDatabase database)
    {
        _database = database;
    }

    public async Task SavePetEntryAsync()
    {
        var entry = new PetEntry
        {
            PetId = 1,
            Date = CurrentSelectedDate,
            Mood = EnteredMood
        };

        await _database.SavePetEntryAsync(entry);
    }

    public async Task LoadEntriesAsync()
    {
        var MoodEntry = await _database.GetPetEntryByDateAsync(CurrentSelectedDate);
        if (MoodEntry == null)
        {
            ShownMood = string.Empty;
            return;
        }
        ShownMood = MoodEntry.Mood;
        return;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}