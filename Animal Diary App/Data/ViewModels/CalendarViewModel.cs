namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
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
    private string enteredWeight = string.Empty;

    public string EnteredWeight
    {
        get => enteredWeight.ToString();
        set
        {
            if (enteredWeight == value) return;
            enteredWeight = value;
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


    public async Task SavePetMoodEntryAsync()
    {
        var ExistingEntry = await _database.GetPetEntryByDateAsync(CurrentSelectedDate);
        if (ExistingEntry != null)
        {
            ExistingEntry.Mood = EnteredMood;
            await _database.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = 1,     // Assuming a single pet for simplicity, Should refactor to support multiple pets in the future R-
            Date = CurrentSelectedDate,
            Mood = EnteredMood
        };

        await _database.SavePetEntryAsync(entry);
    }
    decimal weight = 0;

    private bool TryParseEnteredWeight(out decimal weight)
    {
        if (!decimal.TryParse(EnteredWeight, NumberStyles.Any, CultureInfo.CurrentCulture, out weight))
        {
            Console.WriteLine("Please enter a valid number");
            return false;
        }

        if (weight <= 0)
        {
            Console.WriteLine("Weight must be greater than 0");
            return false;
        }

        return true;
    }

    public async Task SavePetWeightEntryAsync()
    {
        var ExistingEntry = await _database.GetPetEntryByDateAsync(CurrentSelectedDate);
        weight = TryParseEnteredWeight(out weight) ? weight : 0;

        if (weight == 0)
        {
            Console.WriteLine("Invalid weight entry, skipping save.");
            return;
        }
        if (ExistingEntry != null)
        {
            ExistingEntry.Weight = weight;
            await _database.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = 1,     // Assuming a single pet for simplicity, Should refactor to support multiple pets in the future R-
            Date = CurrentSelectedDate,
            Weight = weight,
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