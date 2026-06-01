namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Data.Services.Data.Device;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Collections.Generic;
using Plugin.LocalNotification;


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
        set
        {
            if (SetProperty(ref enteredMedicationName, value))
            {
                OnPropertyChanged(nameof(CanSaveMedication));
                ValidateMedicationName();
            }
        }
    }

    private string enteredDosage = string.Empty;
    public string EnteredDosage
    {
        get => enteredDosage;
        set
        {
            if (SetProperty(ref enteredDosage, value))
            {
                OnPropertyChanged(nameof(CanSaveMedication));
                ValidateDosage();
            }
        }
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
        set
        {
            if (SetProperty(ref selectedFrequency, value))
            {
                OnPropertyChanged(nameof(CanSaveMedication));
            }
        }
    }
    // Validation error messages
    private string medicationNameError = string.Empty;
    public string MedicationNameError
    {
        get => medicationNameError;
        set => SetProperty(ref medicationNameError, value);
    }

    private string dosageError = string.Empty;
    public string DosageError
    {
        get => dosageError;
        set => SetProperty(ref dosageError, value);
    }

    private string daysError = string.Empty;
    public string DaysError
    {
        get => daysError;
        set => SetProperty(ref daysError, value);
    }

    private string petError = string.Empty;
    public string PetError
    {
        get => petError;
        set => SetProperty(ref petError, value);
    }

    // Validation logic
    private bool IsMedicationNameValid => !string.IsNullOrWhiteSpace(MedicationDraft.Name);

    private bool IsDosageValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MedicationDraft.Dosage.ToString()))
                return false;
            return decimal.TryParse(MedicationDraft.Dosage.ToString(), out var dose) && dose > 0;
        }
    }

    private bool AnyDaySelected => Days.Any(d => d.IsSelected);

    private bool IsPetSelected => SelectedMedicationDraftPet != null;

    // Composite validation - true only if all required fields valid
    public bool CanSaveMedication =>
        IsMedicationNameValid &&
        IsDosageValid &&
        AnyDaySelected &&
        IsPetSelected;

    private void ValidateMedicationName()
    {
        MedicationNameError = string.IsNullOrWhiteSpace(MedicationDraft.Name)
            ? "Medication name is required"
            : string.Empty;
    }

    private void ValidateDosage()
    {
        if (string.IsNullOrWhiteSpace(MedicationDraft.Dosage.ToString()))
        {
            DosageError = "Dosage is required";
        }
        else if (!decimal.TryParse(MedicationDraft.Dosage.ToString(), out var dose) || dose <= 0)
        {
            DosageError = "Dosage must be a positive number";
        }
        else
        {
            DosageError = string.Empty;
        }
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
        set
        {
            if (SetProperty(ref selectedMedicationDraftPet, value))
            {
                OnPropertyChanged(nameof(CanSaveMedication));
                ValidatePetSelection();
            }
        }
    }
    private Medication medicationDraft = new();
    public Medication MedicationDraft
    {
        get => medicationDraft;
        set
        {
            if (SetProperty(ref medicationDraft, value))
            {
                // Subscribe to nested property changes
                if (medicationDraft != null)
                {
                    medicationDraft.PropertyChanged -= OnMedicationDraftPropertyChanged;
                    medicationDraft.PropertyChanged += OnMedicationDraftPropertyChanged;
                }
                OnPropertyChanged(nameof(CanSaveMedication));
                ValidateMedicationName();
                ValidateDosage();
            }
        }
    }

    private void OnMedicationDraftPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Medication.Name))
        {
            ValidateMedicationName();
            OnPropertyChanged(nameof(CanSaveMedication));
        }
        else if (e.PropertyName == nameof(Medication.Dosage))
        {
            ValidateDosage();
            OnPropertyChanged(nameof(CanSaveMedication));
        }
    }

    private readonly MedicationService _medicationService;
    private readonly ActivePetService _activePetService;
    private readonly PetService _petService;
    private readonly INotificationService _notificationService;
    public List<string> UnitOptions { get; } = new() { "mg", "ml", "tablet", "drops" };

    public MedicationViewModel(MedicationService medicationService, ActivePetService activePetService, PetService petService, INotificationService notificationService)
    {
        _medicationService = medicationService;
        _activePetService = activePetService;
        _petService = petService;
        _notificationService = notificationService;


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

        // Set defaults
        SelectedFrequency = 1;
        SelectedTime = new TimeSpan(8, 0, 0);  // 8:00 AM
        //SelectedMedicationDraftPet = ;  // Set active pet as default
    }
    public async Task SetSelectedMedicationDraftAsync()
    {
        SelectedMedicationDraftPet = await _petService.GetPetByIdAsync(await _activePetService.GetSavedActivePetIdAsync());
        Console.WriteLine($"Set SelectedMedicationDraftPet to active pet: {SelectedMedicationDraftPet?.Name}");
    }

    private TimeSpan selectedTime;
    public TimeSpan SelectedTime
    {
        get => selectedTime;
        set => SetProperty(ref selectedTime, value);
    }

    public async Task SaveMedicationCommandasync()
    {
        // Validate before saving
        ValidateMedicationName();
        ValidateDosage();
        ValidatePetSelection();
        ValidateDaysSelected();

        // Prevent save if validation fails
        if (!CanSaveMedication)
        {
            return;
        }

        var newMedication = new Medication
        {
            Name = MedicationDraft.Name,
            Dosage = ParseDosage(),
            Unit = MedicationDraft.Unit,
            PetId = SelectedMedicationDraftPet!.Id,
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
        await _notificationService.RequestNotificationPermission();
        await _notificationService.ScheduleNotification("Medication Reminder", "Give Max his insulin", DateTime.Now.AddSeconds(10)); // Example: Schedule notification for 10 seconds from now
    }

    public ICommand SaveMedicationCommand => new Command(async () =>
    {
        await SaveMedicationCommandasync();
    });
    public ICommand CancelMedicationCommand => new Command(() =>
    {
        ClearMedicationDraft();

        OnMedicationSaved?.Invoke(this, EventArgs.Empty);
    });

    private void ClearMedicationDraft()
    {
        MedicationDraft = new();
        EnteredMedicationName = string.Empty;
        EnteredDosage = string.Empty;
        SelectedMedicationDraftPet = _activePetService.ActivePet;  // Reset to active pet
        SelectedFrequency = 1;  // Reset to 1
        SelectedTime = new TimeSpan(8, 0, 0);  // Reset to 8:00 AM
        foreach (var day in Days)
        {
            day.IsSelected = false;
        }

        Times.Clear();
        Times.Add(new MedicationSchedule { Time = new TimeSpan(8, 0, 0) });
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
        OnPropertyChanged(nameof(CanSaveMedication));
        ValidateDaysSelected();
    }

    private void ValidatePetSelection()
    {
        PetError = SelectedMedicationDraftPet == null
            ? "Please select a pet for this medication"
            : string.Empty;
    }

    private void ValidateDaysSelected()
    {
        DaysError = !AnyDaySelected
            ? "Please select at least one day"
            : string.Empty;
    }


}