namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Collections.ObjectModel;

public class CalendarViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private DateTime currentSelectedDate = DateTime.Now.Date;
    public DateTime CurrentSelectedDate
    {
        get => currentSelectedDate;
        set
        {
            var newDate = value.Date;
            if (currentSelectedDate == newDate)
                return;

            currentSelectedDate = newDate;
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
    private string shownWeight = string.Empty;
    public string ShownWeight
    {
        get => shownWeight;
        set
        {
            if (shownWeight == value) return;
            shownWeight = value;
            OnPropertyChanged();
        }
    }

    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    public MedicationViewModel MedicationVM { get; }

    public CalendarViewModel(
    PetEntryService petEntryService,
    MedicationViewModel medicationVM, PetService petService)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        MedicationVM = medicationVM;

    }
    public ObservableCollection<Pet> Pets { get; set; } = new ObservableCollection<Pet>();

    private async Task LoadPetsAsync()
    {
        Pets.Clear();
        var petsFromDb = await _petService.GetPetsAsync();

        foreach (var pet in petsFromDb)
        {
            Pets.Add(pet);
        }
        if (petsFromDb.Count == 0)
            return;

        var selected = petsFromDb.FirstOrDefault(p => p.Id == CurrentPetId) ?? petsFromDb[0];
        CurrentPetId = selected.Id;
        selected.IsSelected = true;
    }
    public async Task SavePetMoodEntryAsync()
    {
        var ExistingEntry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(CurrentSelectedDate, CurrentPetId);
        if (ExistingEntry != null)
        {
            ExistingEntry.Mood = EnteredMood;
            await _petEntryService.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = CurrentPetId,
            Date = CurrentSelectedDate,
            Mood = EnteredMood
        };

        await _petEntryService.SavePetEntryAsync(entry);
    }
    decimal weight = 0;


    public async Task SavePetWeightEntryAsync()
    {
        var ExistingEntry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(CurrentSelectedDate, CurrentPetId);

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
            PetId = CurrentPetId,
            Date = CurrentSelectedDate,
            Weight = weight,
        };

        await _petEntryService.SavePetEntryAsync(entry);
    }

    public async Task LoadEntriesAsync()
    {
        var Entries = await _petEntryService.GetPetEntryByDateAndPetIdAsync(CurrentSelectedDate, CurrentPetId);
        if (Entries == null)
        {
            ShownMood = string.Empty;
            ShownWeight = string.Empty;
            return;
        }
        ShownMood = Entries.Mood;
        ShownWeight = Entries.Weight.ToString();
        return;
    }

    public EntrySection MoodSection { get; } = new();
    public EntrySection WeightSection { get; } = new();
    public EntrySection MedicationSection { get; } = new();


    /// <summary>
    /// Loads pets and entries. Call from Main while that page is visible so Calendar opens ready.
    /// </summary>
    public async Task PrepareDataAsync()
    {
        ResetInputSections();
        await Task.WhenAll(LoadPetsAsync(), LoadEntriesAsync());
    }

    /// <summary>
    /// Light refresh when returning to Calendar (entries only; pets already loaded).
    /// </summary>
    public async Task RefreshEntriesAsync()
    {
        ResetInputSections();
        await LoadEntriesAsync();
    }

    private void ResetInputSections()
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

    public int CurrentPetId = 1;
    public ICommand SelectPetCommand => new Command<Pet>(async pet =>
    {
        foreach (var p in Pets)
        {
            p.IsSelected = false;
        }
        pet.IsSelected = true;
        CurrentPetId = pet.Id;
        await LoadEntriesAsync();
        Console.WriteLine($"Selected pet: {pet.Name}");
    });



    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}