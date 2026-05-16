namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows.Input;
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

    private readonly PetEntryService _petEntryService;
    public MedicationViewModel MedicationVM { get; }

    public CalendarViewModel(
    PetEntryService petEntryService,
    MedicationViewModel medicationVM)
    {
        _petEntryService = petEntryService;
        MedicationVM = medicationVM;
    }


    public async Task SavePetMoodEntryAsync()
    {
        var ExistingEntry = await _petEntryService.GetPetEntryByDateAsync(CurrentSelectedDate);
        if (ExistingEntry != null)
        {
            ExistingEntry.Mood = EnteredMood;
            await _petEntryService.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = 1,     // Assuming a single pet for simplicity, Should refactor to support multiple pets in the future R-
            Date = CurrentSelectedDate,
            Mood = EnteredMood
        };

        await _petEntryService.SavePetEntryAsync(entry);
    }
    decimal weight = 0;


    public async Task SavePetWeightEntryAsync()
    {
        var ExistingEntry = await _petEntryService.GetPetEntryByDateAsync(CurrentSelectedDate);

        if (!InputParser.TryParsePositive(EnteredWeight, out weight))
        {
            Console.WriteLine("Invalid weight entry, skipping save.");
            return;
        }
        if (ExistingEntry != null)
        {
            ExistingEntry.Weight = weight;
            await _petEntryService.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = 1,     // Assuming a single pet for simplicity, Should refactor to support multiple pets in the future R-
            Date = CurrentSelectedDate,
            Weight = weight,
        };

        await _petEntryService.SavePetEntryAsync(entry);
    }

    public async Task LoadEntriesAsync()
    {
        var MoodEntry = await _petEntryService.GetPetEntryByDateAsync(CurrentSelectedDate);
        if (MoodEntry == null)
        {
            ShownMood = string.Empty;
            return;
        }
        ShownMood = MoodEntry.Mood;
        return;
    }

    public EntrySection MoodSection { get; } = new();
    public EntrySection WeightSection { get; } = new();
    public EntrySection MedicationSection { get; } = new();


    public async Task InitCalendarPageAsync()
    {
        EntrySection.HideInput(MoodSection);
        EntrySection.HideInput(WeightSection);
        EntrySection.HideInput(MedicationSection);
    }
    public ICommand ShowMoodInputCommand => new Command(() =>
    {
        EntrySection.ShowInput(MoodSection);
    });
    public ICommand OnMoodEntryCompleted =>
    new Command(async () =>
    {
        EntrySection.HideInput(MoodSection);
        await SavePetMoodEntryAsync();
        await LoadEntriesAsync();
        EnteredMood = "";
    });

    public ICommand ShowWeightInputCommand => new Command(() =>
    {
        EntrySection.ShowInput(WeightSection);
    });

    public ICommand OnWeightEntryCompleted => new Command(async () =>
    {
        EntrySection.HideInput(WeightSection);
        await SavePetWeightEntryAsync();
        await LoadEntriesAsync();
        EnteredWeight = "";
    });



    public ICommand ShowMedicationInputCommand => new Command(() =>
    {
        EntrySection.ShowInput(MedicationSection);
    });

    public ICommand SaveMedicationEntryCommand => new Command(async () =>
    {
        await MedicationVM.SaveMedicationEntryAsync();
        EntrySection.HideInput(MedicationSection);
    });


    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}