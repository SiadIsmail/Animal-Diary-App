namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class CalendarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;


    private string enteredMood = string.Empty;
    private DateTime currentSelectedDate = DateTime.Now;
    public DateTime CurrentSelectedDate
    {
        get => currentSelectedDate;
        set
        {
            currentSelectedDate = value;
            OnPropertyChanged();

        }
    }

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
        Entries = await _database.GetPetEntriesAsync();
        var CurrentMood = CurrentSelectedDate
        var MoodEntry = await db.Table(PetEntry)
        
        OnPropertyChanged(nameof(Entries));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}