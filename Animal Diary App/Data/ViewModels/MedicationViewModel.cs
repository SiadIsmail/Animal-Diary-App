namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Collections.Generic;


public class MedicationViewModel : BaseViewModel, IResettableDraft
{
    private decimal ParseDosage()
    {
        if (InputParser.TryParsePositive(MedicationDraft.Dosage.ToString(), out var dosage))
        {
            return dosage;
        }
        return 0;
    }
    // ── Add/Edit sheet state ──────────────────────────────────────────────
    // The Add/Edit medication form is shown as a slide-up sheet overlaying the
    // Medications page. This flag drives its visibility (and the scrim/dim +
    // slide animation wired up in the view).
    private bool isAddEditSheetVisible;
    public bool IsAddEditSheetVisible
    {
        get => isAddEditSheetVisible;
        set => SetProperty(ref isAddEditSheetVisible, value);
    }

    // True while editing an existing medication (vs. adding a new one). Drives
    // the sheet title and the save-button label ("Override" vs. "Save").
    private bool isEditingMedication;
    public bool IsEditingMedication
    {
        get => isEditingMedication;
        set
        {
            if (SetProperty(ref isEditingMedication, value))
            {
                OnPropertyChanged(nameof(SheetTitle));
                OnPropertyChanged(nameof(SheetSubtitle));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    // Id of the medication currently being edited (null when adding).
    private int? editingMedicationId;

    public string SheetTitle => LocalizationManager.Instance.GetString(IsEditingMedication ? "MedEdit_EditTitle" : "MedEdit_AddTitle");

    /// <summary>The sheet's Caveat subtitle: the gentle editing note while editing,
    /// empty when adding (the shared sheet hides an empty subtitle).</summary>
    public string SheetSubtitle => IsEditingMedication ? LocalizationManager.Instance.GetString("MedEdit_EditingNote") : string.Empty;

    public string SaveButtonText => LocalizationManager.Instance.GetString(IsEditingMedication ? "MedEdit_Override" : "MedEdit_Save");

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
                SyncReminderTimesToFrequency();
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
            ? LocalizationManager.Instance.GetString("Validation_MedNameRequired")
            : string.Empty;
    }

    private void ValidateDosage()
    {
        if (string.IsNullOrWhiteSpace(MedicationDraft.Dosage.ToString()))
        {
            DosageError = LocalizationManager.Instance.GetString("Validation_DosageRequired");
        }
        else if (!decimal.TryParse(MedicationDraft.Dosage.ToString(), out var dose) || dose <= 0)
        {
            DosageError = LocalizationManager.Instance.GetString("Validation_DosagePositive");
        }
        else
        {
            DosageError = string.Empty;
        }
    }

    public ObservableCollection<FilteredMedication> FilteredMedications { get; set; } = new ObservableCollection<FilteredMedication>();

    // Which tab the Medications list is showing: "active" or "archived".
    private string activeTab = "active";
    public string ActiveTab
    {
        get => activeTab;
        set => SetProperty(ref activeTab, value);
    }

    public ICommand SetActiveTabCommand { get; }

    public async Task LoadFilteredMedicationAsync()
    {
        FilteredMedications.Clear();
        var showArchived = ActiveTab == "archived";
        var petId = await _activePetService.GetSavedActivePetIdAsync();
        List<Medication> medicationFromDb = await _medicationService.GetMedicationsByPetIdAsync(petId);
        var visible = medicationFromDb.Where(m => m.IsArchived == showArchived).ToList();

        // Every med in the list belongs to the same pet, and the schedule rows come
        // in one batched IN-clause query — no per-medication round trips.
        var pet = await _petService.GetPetByIdAsync(petId);
        var schedulesByMed = (await _medicationService.GetSchedulesForMedicationsAsync(visible.Select(m => m.Id).ToList()))
            .ToLookup(s => s.MedicationId);

        foreach (var medication in visible)
        {
            var distinctTimes = schedulesByMed[medication.Id].Select(s => s.Time).Distinct().OrderBy(t => t).ToList();
            var timesPerDay = distinctTimes.Count;
            FilteredMedications.Add(new FilteredMedication
            {
                Id = medication.Id,
                Name = medication.Name,
                PetName = pet?.Name ?? LocalizationManager.Instance.GetString("Med_Unknown"),
                DoseDisplay = $"{medication.Dosage} {medication.Unit}",
                FrequencyDisplay = timesPerDay <= 1
                    ? LocalizationManager.Instance.GetString("Med_OnceDaily")
                    : LocalizationManager.Instance.Format("Med_TimesDaily", timesPerDay),
                TimesDisplay = string.Join(" · ", distinctTimes.Select(t => t.ToString(@"hh\:mm"))),
                Note = medication.Notes
            });
        }
    }

    /// <summary>
    /// Archive (or restore) a medication. Archiving cancels its reminders;
    /// restoring re-schedules them from the saved times. Demonstrates the
    /// notification system's cancel/update lifecycle.
    /// </summary>
    public ICommand ArchiveMedicationCommand { get; }

    private async Task ArchiveMedicationAsync(FilteredMedication? filtered)
    {
        if (filtered == null)
            return;

        var medication = await _medicationService.GetMedicationByIdAsync(filtered.Id);
        if (medication == null)
            return;

        medication.IsArchived = !medication.IsArchived;
        await _medicationService.UpdateMedicationAsync(medication);

        // Archiving cancels reminders; restoring re-materializes them from the
        // saved schedule. SyncMedicationAsync handles the archived case too.
        if (medication.IsArchived)
            await _reminderScheduler.CancelMedicationAsync(medication.Id);
        else
            await _reminderScheduler.SyncMedicationAsync(medication.Id);

        await LoadFilteredMedicationAsync();
    }

    /// <summary>
    /// Open the slide-up sheet to add a new medication. Starts from a blank
    /// draft seeded with the active pet.
    /// </summary>
    public ICommand AddMedicationCommand { get; }

    private async Task AddMedicationSheetAsync()
    {
        ClearMedicationDraft();
        editingMedicationId = null;
        IsEditingMedication = false;
        await SetSelectedMedicationDraftAsync();
        IsAddEditSheetVisible = true;
    }

    /// <summary>
    /// Open the slide-up sheet to edit an existing medication. Loads the saved
    /// values (name, dose, unit, notes, days and reminder times) into the draft
    /// so the form opens pre-filled; saving overrides the original.
    /// </summary>
    public ICommand EditMedicationCommand { get; }

    private async Task EditMedicationSheetAsync(FilteredMedication? filtered)
    {
        if (filtered == null)
            return;

        var medication = await _medicationService.GetMedicationByIdAsync(filtered.Id);
        if (medication == null)
            return;

        editingMedicationId = medication.Id;
        IsEditingMedication = true;

        // Pre-fill the draft from the saved medication.
        MedicationDraft = new Medication
        {
            Id = medication.Id,
            PetId = medication.PetId,
            Name = medication.Name,
            Dosage = medication.Dosage,
            Unit = medication.Unit,
            Notes = medication.Notes,
            IsArchived = medication.IsArchived
        };
        SelectedMedicationDraftPet = await _petService.GetPetByIdAsync(medication.PetId);

        // Rebuild day + reminder-time selections from the saved schedule rows.
        var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(medication.Id);
        var selectedDays = schedules.Select(s => s.Day).Distinct().ToHashSet();
        foreach (var day in Days)
            day.IsSelected = selectedDays.Contains(day.Day);

        var distinctTimes = schedules.Select(s => s.Time).Distinct().OrderBy(t => t).ToList();

        // Setting the frequency rebuilds the reminder-time slots; then overwrite
        // each slot with the saved time.
        SelectedFrequency = Math.Clamp(distinctTimes.Count, 1, MedicationReminderScheduler.MaxReminderTimes);
        for (var i = 0; i < ReminderTimes.Count && i < distinctTimes.Count; i++)
            ReminderTimes[i].Time = distinctTimes[i];

        ValidateDaysSelected();
        OnPropertyChanged(nameof(CanSaveMedication));

        IsAddEditSheetVisible = true;
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
            // Unhook the OLD draft before SetProperty swaps the field — the
            // previous version unsubscribed from the new value (a no-op).
            var previous = medicationDraft;
            if (SetProperty(ref medicationDraft, value))
            {
                if (previous != null)
                    previous.PropertyChanged -= OnMedicationDraftPropertyChanged;
                if (medicationDraft != null)
                    medicationDraft.PropertyChanged += OnMedicationDraftPropertyChanged;
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
    private readonly MedicationReminderScheduler _reminderScheduler;
    private readonly IAnalyticsService _analytics;
    public List<string> UnitOptions { get; } = new() { "mg", "ml", "tablet", "drops" };

    public MedicationViewModel(MedicationService medicationService, ActivePetService activePetService, PetService petService, MedicationReminderScheduler reminderScheduler, IAnalyticsService analytics)
    {
        _medicationService = medicationService;
        _activePetService = activePetService;
        _petService = petService;
        _reminderScheduler = reminderScheduler;
        _analytics = analytics;


        Days = new ObservableCollection<DaySelectionItem>
        {
            new () { Day = DayOfWeek.Monday, ResourceKey = "Day_Mon" },
            new () { Day = DayOfWeek.Tuesday, ResourceKey = "Day_Tue" },
            new () { Day = DayOfWeek.Wednesday, ResourceKey = "Day_Wed" },
            new () { Day = DayOfWeek.Thursday, ResourceKey = "Day_Thu" },
            new () { Day = DayOfWeek.Friday, ResourceKey = "Day_Fri" },
            new () { Day = DayOfWeek.Saturday, ResourceKey = "Day_Sat" },
            new () { Day = DayOfWeek.Sunday, ResourceKey = "Day_Sun" }
        };

        // This VM is a singleton, so a live language switch must re-raise the
        // day names (they resolve per read; the bindings just need the nudge).
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var day in Days)
                day.RefreshDisplayName();
        };

        ToggleDayCommand = new Command<DaySelectionItem>(ToggleDay);

        // Commands are created once (never as expression-bodied `=> new Command(...)`
        // properties, which hand out a fresh instance per binding read).
        SetActiveTabCommand = new Command<string>(async tab =>
        {
            if (string.IsNullOrWhiteSpace(tab) || tab == ActiveTab)
                return;
            ActiveTab = tab;
            await LoadFilteredMedicationAsync();
        });
        ArchiveMedicationCommand = new Command<FilteredMedication>(async filtered => await ArchiveMedicationAsync(filtered));
        AddMedicationCommand = new Command(async () => await AddMedicationSheetAsync());
        EditMedicationCommand = new Command<FilteredMedication>(async filtered => await EditMedicationSheetAsync(filtered));
        SaveMedicationCommand = new Command(async () => await SaveMedicationCommandasync());
        CancelMedicationCommand = new Command(() =>
        {
            ClearMedicationDraft();
            editingMedicationId = null;
            IsEditingMedication = false;
            IsAddEditSheetVisible = false;
        });

        // Set defaults — SelectedFrequency drives how many reminder-time pickers
        // are shown (see SyncReminderTimesToFrequency).
        SelectedFrequency = 1;
    }
    public async Task SetSelectedMedicationDraftAsync()
    {
        SelectedMedicationDraftPet = await _petService.GetPetByIdAsync(await _activePetService.GetSavedActivePetIdAsync());
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

        var pet = SelectedMedicationDraftPet!;

        // Distinguish create from edit up front so the analytics event fires only for a
        // brand-new medication (an edit re-uses SaveMedication... but isn't a "created").
        var isNewMedication = !(IsEditingMedication && editingMedicationId.HasValue);

        Medication medication;

        if (IsEditingMedication && editingMedicationId.HasValue)
        {
            // Override the existing medication in place.
            var existing = await _medicationService.GetMedicationByIdAsync(editingMedicationId.Value);
            if (existing == null)
                return;

            existing.Name = MedicationDraft.Name;
            existing.Dosage = ParseDosage();
            existing.Unit = MedicationDraft.Unit;
            existing.PetId = pet.Id;
            existing.Notes = MedicationDraft.Notes;
            medication = existing;
        }
        else
        {
            medication = new Medication
            {
                Name = MedicationDraft.Name,
                Dosage = ParseDosage(),
                Unit = MedicationDraft.Unit,
                PetId = pet.Id,
                Notes = MedicationDraft.Notes,
                CreatedAt = DateTime.Now
            };
        }

        // One schedule row for every (selected day × reminder time); the medication
        // and its full schedule set persist in a single transaction so an interrupted
        // save can't leave the medication without its rules.
        var schedules = Days.Where(d => d.IsSelected)
            .SelectMany(day => ReminderTimes.Select(t => new MedicationSchedule
            {
                Day = day.Day,
                Time = t.Time
            }))
            .ToList();
        await _medicationService.SaveMedicationWithSchedulesAsync(medication, schedules);
        var medicationId = medication.Id;

        // "Which features provide value?" — record that a medication was set up, with
        // only the non-identifying shape of its schedule (how many reminders per day,
        // how many weekdays). Never the name, dose, unit, or notes.
        if (isNewMedication)
        {
            _analytics.Track(AnalyticsEvents.MedicationCreated, new Dictionary<string, object?>
            {
                [AnalyticsEvents.PropReminderCount] = ReminderTimes.Count,
                [AnalyticsEvents.PropDaysPerWeek] = Days.Count(d => d.IsSelected),
            });
        }

        ClearMedicationDraft();
        editingMedicationId = null;
        IsEditingMedication = false;
        IsAddEditSheetVisible = false;

        // The scheduler reads the just-saved schedule rules and materializes
        // concrete reminder occurrences from them. SyncMedicationAsync is
        // idempotent, so it doubles as the "update after edit" path.
        var permissionGranted = await _reminderScheduler.RequestPermissionAsync();
        if (!permissionGranted)
        {
            System.Diagnostics.Debug.WriteLine("[MedicationViewModel] Exact-alarm permission was not granted; reminders will use the platform fallback.");
        }

        await _reminderScheduler.SyncMedicationAsync(medicationId);

        await LoadFilteredMedicationAsync();
    }

    public ICommand SaveMedicationCommand { get; }
    public ICommand CancelMedicationCommand { get; }

    /// <summary>
    /// Full reset of the add/edit medication form: clears the draft, drops any
    /// in-progress edit, hides the sheet, and wipes validation messages. Used by
    /// the global data reset (<c>MainViewModel.ResetDrafts</c>); the per-action
    /// flows use the narrower <see cref="ClearMedicationDraft"/> plus their own
    /// flag resets.
    /// </summary>
    public void ResetDraft()
    {
        ClearMedicationDraft();
        editingMedicationId = null;
        IsEditingMedication = false;
        IsAddEditSheetVisible = false;

        MedicationNameError = string.Empty;
        DosageError = string.Empty;
        DaysError = string.Empty;
        PetError = string.Empty;
    }

    private void ClearMedicationDraft()
    {
        MedicationDraft = new();
        SelectedMedicationDraftPet = _activePetService.ActivePet;  // Reset to active pet
        SelectedFrequency = 1;  // Reset to 1 (also rebuilds ReminderTimes)
        foreach (var day in Days)
        {
            day.IsSelected = false;
        }

        SyncReminderTimesToFrequency();
    }


    public ObservableCollection<DaySelectionItem> Days { get; set; }

    public ICommand ToggleDayCommand { get; set; }

    /// <summary>
    /// One entry per daily reminder time. The collection is kept in sync with
    /// <see cref="SelectedFrequency"/> so the UI shows exactly that many
    /// <see cref="TimePicker"/>s.
    /// </summary>
    public ObservableCollection<ReminderTimeSlot> ReminderTimes { get; } = new();

    // Sensible spread of default times as the user adds more daily reminders.
    private static readonly TimeSpan[] DefaultReminderTimes =
    {
        new(8, 0, 0),   // morning
        new(20, 0, 0),  // evening
        new(13, 0, 0),  // midday
        new(17, 0, 0),  // late afternoon
        new(22, 0, 0)   // night
    };

    private static TimeSpan DefaultTimeForSlot(int slot)
        => DefaultReminderTimes[Math.Clamp(slot, 0, DefaultReminderTimes.Length - 1)];

    /// <summary>
    /// Grow or shrink <see cref="ReminderTimes"/> to match the chosen frequency
    /// (capped at <see cref="MedicationReminderScheduler.MaxReminderTimes"/>),
    /// preserving the times the user has already picked.
    /// </summary>
    private void SyncReminderTimesToFrequency()
    {
        var target = Math.Clamp(SelectedFrequency, 1, MedicationReminderScheduler.MaxReminderTimes);

        while (ReminderTimes.Count > target)
            ReminderTimes.RemoveAt(ReminderTimes.Count - 1);

        while (ReminderTimes.Count < target)
            ReminderTimes.Add(new ReminderTimeSlot(ReminderTimes.Count, DefaultTimeForSlot(ReminderTimes.Count)));

        for (var i = 0; i < ReminderTimes.Count; i++)
            ReminderTimes[i].Index = i;
    }

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
            ? LocalizationManager.Instance.GetString("Validation_SelectPet")
            : string.Empty;
    }

    private void ValidateDaysSelected()
    {
        DaysError = !AnyDaySelected
            ? LocalizationManager.Instance.GetString("Validation_SelectDay")
            : string.Empty;
    }


}