namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Collections.Generic;

public class MedicationViewModel : BaseViewModel
{
    private decimal ParseDosage()
    {
        if (InputParser.TryParsePositive(MedicationDraft.Dosage.ToString(), out var dosage))
        {
            return dosage;
        }
        return 0;
    }
    private string enteredMedicationName = string.Empty;
    public string EnteredMedicationName
    {
        get => enteredMedicationName;
        set => SetProperty(ref enteredMedicationName, value);
    }

    private string enteredDosage = string.Empty;
    public string EnteredDosage
    {
        get => enteredDosage;
        set => SetProperty(ref enteredDosage, value);
    }


    public List<int> FrequencyOptions { get; } = new()
    {
        1,
        2,
        3,
        4,
        5
    };


    private int selectedFrequency;
    public int SelectedFrequency
    {
        get => selectedFrequency;
        set => SetProperty(ref selectedFrequency, value);
    }
    public ObservableCollection<FilteredMedication> FilteredMedications { get; set; } = new ObservableCollection<FilteredMedication>();


    public async Task LoadFilteredMedicationAsync()
    {
        FilteredMedications.Clear();
        List<Medication> medicationFromDb = await _medicationService.GetMedicationsByPetIdAsync(await _activePetService.GetSavedActivePetIdAsync());

        foreach (var medication in medicationFromDb)
        {
            var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(medication.Id);
            var times = schedules.Select(s => s.Time).ToList();
            var frequency = schedules.Count;
            var pet = await _petService.GetPetByIdAsync(medication.PetId);
            FilteredMedications.Add(new FilteredMedication
            {
                Id = medication.Id,
                Name = medication.Name,
                PetName = pet?.Name ?? "Unknown",
                DoseDisplay = $"{medication.Dosage} mg",
                FrequencyDisplay = $"{frequency} Times a day",
                TimesDisplay = times.FirstOrDefault(),
                Note = medication.Notes
            });
        }
    }
    private Pet? selectedMedicationDraftPet;
    public Pet? SelectedMedicationDraftPet
    {
        get => selectedMedicationDraftPet;
        set => SetProperty(ref selectedMedicationDraftPet, value);
    }
    private Medication medicationDraft = new();
    public Medication MedicationDraft
    {
        get => medicationDraft;
        set => SetProperty(ref medicationDraft, value);
    }

    private readonly MedicationService _medicationService;
    private readonly ActivePetService _activePetService;
    private readonly PetService _petService;
    public List<string> UnitOptions { get; } = new() { "mg", "ml", "tablet", "drops" };

    public MedicationViewModel(MedicationService medicationService, ActivePetService activePetService, PetService petService)
    {
        _medicationService = medicationService;
        _activePetService = activePetService;
        _petService = petService;
    

        Days = new ObservableCollection<DaySelectionItem>
        {
            new () { Day = DayOfWeek.Monday, DisplayName = "Mo" },
            new () { Day = DayOfWeek.Tuesday, DisplayName = "Tu" },
            new () { Day = DayOfWeek.Wednesday, DisplayName = "We" },
            new () { Day = DayOfWeek.Thursday, DisplayName = "Th" },
            new () { Day = DayOfWeek.Friday, DisplayName = "Fr" },
            new () { Day = DayOfWeek.Saturday, DisplayName = "Sa" },
            new () { Day = DayOfWeek.Sunday, DisplayName = "Su" }
        };
        Times = new ObservableCollection<MedicationSchedule>
        {
            new MedicationSchedule
            {
        Time = new TimeSpan(8, 0, 0)
            }
        };

        ToggleDayCommand = new Command<DaySelectionItem>(ToggleDay);
    }


    private TimeSpan selectedTime;
    public TimeSpan SelectedTime
    {
        get => selectedTime;
        set => SetProperty(ref selectedTime, value);
    }

    public async Task SaveMedicationCommandasync()
    {
        var newMedication = new Medication
        {
            Name = MedicationDraft.Name,
            Dosage = ParseDosage(),
            PetId = SelectedMedicationDraftPet?.Id ?? 0,
            Notes = MedicationDraft.Notes
        };
        await _medicationService.SaveMedicationAsync(newMedication);
        var selectedDays = Days.Where(d => d.IsSelected).ToList();
        foreach (var day in selectedDays)
        {

            var schedule = new MedicationSchedule
            {
                MedicationId = newMedication.Id,
                Day = day.Day,
                Time = SelectedTime
            };

            await _medicationService.SaveMedicationScheduleAsync(schedule);

        }
        ClearMedicationDraft();
        OnMedicationSaved?.Invoke(this, EventArgs.Empty);
    }

    public ICommand SaveMedicationCommand => new Command(async () =>
    {
        await SaveMedicationCommandasync();
    });

    private void ClearMedicationDraft()
    {
        MedicationDraft = new();
        EnteredMedicationName = string.Empty;
        EnteredDosage = string.Empty;
        SelectedMedicationDraftPet = null;
        SelectedFrequency = 0;
        foreach (var day in Days)
        {
            day.IsSelected = false;
        }

        Times.Clear();
    }


    public event EventHandler? OnMedicationSaved;

    public ObservableCollection<DaySelectionItem> Days { get; set; }

    public ICommand ToggleDayCommand { get; set; }

    public ObservableCollection<MedicationSchedule> Times { get; set; } = new();

    private void ToggleDay(DaySelectionItem item)
    {
        if (item == null)
            return;

        item.IsSelected = !item.IsSelected;
    }
}