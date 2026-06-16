namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.Windows.Input;
using System.Collections.ObjectModel;

public class CalendarViewModel : BaseViewModel
{
    private DateTime currentSelectedDate = DateTime.Now.Date;
    public DateTime CurrentSelectedDate
    {
        get => currentSelectedDate;
        set
        {
            var newDate = value.Date;
            if (currentSelectedDate == newDate)
                return;

            // The week strip follows the selection; only reload the week's dots
            // when the selection actually crosses into a different week.
            var weekChanged = StartOfWeek(newDate) != StartOfWeek(currentSelectedDate);

            currentSelectedDate = newDate;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedDateDisplay));
            _ = LoadEntriesAsync();
            _ = LoadDosesAsync();
            if (weekChanged)
                _ = LoadWeekActivitiesAsync();
        }
    }

    /// <summary>Monday on/before the given date (week strips start on Monday).</summary>
    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-offset);
    }

    private string enteredMood = string.Empty;

    public string EnteredMood
    {
        get => enteredMood;
        set => SetProperty(ref enteredMood, value);
    }

    private MoodLevel selectedMoodLevel = MoodLevel.None;
    public MoodLevel SelectedMoodLevel
    {
        get => selectedMoodLevel;
        set => SetProperty(ref selectedMoodLevel, value);
    }

    private string enteredWeight = string.Empty;

    public string EnteredWeight
    {
        get => enteredWeight;
        set => SetProperty(ref enteredWeight, value);
    }
    private string shownMood = string.Empty;
    public string ShownMood
    {
        get => shownMood;
        set
        {
            if (SetProperty(ref shownMood, value))
            {
                OnPropertyChanged(nameof(ShownMoodDisplay));
                OnPropertyChanged(nameof(HasMood));
            }
        }
    }
    private string shownWeight = string.Empty;
    public string ShownWeight
    {
        get => shownWeight;
        set
        {
            if (SetProperty(ref shownWeight, value))
            {
                OnPropertyChanged(nameof(ShownWeightDisplay));
                OnPropertyChanged(nameof(HasWeight));
            }
        }
    }

    /// <summary>Localized "Selected Date: …" label for the calendar.</summary>
    public string SelectedDateDisplay =>
        LocalizationManager.Instance.Format("Calendar_SelectedDate", CurrentSelectedDate);

    public bool HasMood => !string.IsNullOrEmpty(ShownMood);

    /// <summary>Recorded mood, or a localized placeholder when none is logged.</summary>
    public string ShownMoodDisplay =>
        HasMood ? ShownMood : LocalizationManager.Instance.GetString("Calendar_NoMood");

    public bool HasWeight => !string.IsNullOrEmpty(ShownWeight);

    /// <summary>Recorded weight (with unit), or a localized placeholder when none is logged.</summary>
    public string ShownWeightDisplay =>
        HasWeight
            ? ShownWeight + LocalizationManager.Instance.GetString("Common_KgSuffix")
            : LocalizationManager.Instance.GetString("Calendar_NoWeight");

    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly MedicationService _medicationService;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler _reminderScheduler;
    public MedicationViewModel MedicationVM { get; }

    public CalendarViewModel(
    PetEntryService petEntryService,
    MedicationViewModel medicationVM, PetService petService, ActivePetService activePetService,
    MedicationService medicationService, MedicationDoseLogService doseLogService,
    Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler reminderScheduler)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _medicationService = medicationService;
        _doseLogService = doseLogService;
        _reminderScheduler = reminderScheduler;
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

        var savedPetId = await _activePetService.GetSavedActivePetIdAsync();
        var selected = Pets.FirstOrDefault(p => p.Id == savedPetId) ?? petsFromDb[0];
        _activePetService.ActivePet = selected;
        selected.IsSelected = true;
    }
    public async Task SavePetMoodEntryAsync()
    {
        var ExistingEntry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(CurrentSelectedDate, CurrentPetId);
        if (ExistingEntry != null)
        {
            ExistingEntry.MoodLevel = (int)SelectedMoodLevel;
            ExistingEntry.Mood = SelectedMoodLevel.GetDisplayName();
            await _petEntryService.UpdatePetEntryAsync(ExistingEntry);
            return;
        }
        var entry = new PetEntry
        {
            PetId = CurrentPetId,
            Date = CurrentSelectedDate,
            MoodLevel = (int)SelectedMoodLevel,
            Mood = SelectedMoodLevel.GetDisplayName()
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
            SelectedMoodLevel = MoodLevel.None;
            return;
        }
        // Derive the displayed mood from the stored level so it always renders in
        // the active language (the stored Mood string may be in another language).
        ShownMood = Entries.MoodLevel > 0 ? ((MoodLevel)Entries.MoodLevel).GetDisplayName() : string.Empty;
        ShownWeight = Entries.Weight > 0 ? Entries.Weight.ToString() : string.Empty;
        SelectedMoodLevel = (MoodLevel)Entries.MoodLevel;
        return;
    }

    // ── Medication dose checklist (selected date) ────────────────────────
    public ObservableCollection<DoseItem> DosesForSelectedDate { get; } = new();

    private bool hasNoDoses = true;
    public bool HasNoDoses
    {
        get => hasNoDoses;
        set => SetProperty(ref hasNoDoses, value);
    }

    /// <summary>
    /// Build the list of scheduled doses for the active pet on the selected date
    /// by filtering each medication's schedule rules to this weekday, then
    /// attaching any recorded outcome from the dose log.
    /// </summary>
    public async Task LoadDosesAsync()
    {
        DosesForSelectedDate.Clear();

        var petId = CurrentPetId;
        var date = CurrentSelectedDate.Date;

        if (petId != 0)
        {
            var weekday = date.DayOfWeek;
            var now = DateTime.Now;

            var meds = (await _medicationService.GetMedicationsByPetIdAsync(petId))
                .Where(m => !m.IsArchived)
                .ToList();
            var logs = await _doseLogService.GetByPetAndDateAsync(petId, date);

            var items = new List<DoseItem>();
            foreach (var med in meds)
            {
                var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(med.Id);
                var times = schedules.Where(s => s.Day == weekday).Select(s => s.Time).Distinct();

                foreach (var time in times)
                {
                    var log = logs.FirstOrDefault(l => l.MedicationId == med.Id && l.ScheduledTime == time);
                    items.Add(new DoseItem
                    {
                        MedicationId = med.Id,
                        PetId = petId,
                        ScheduledDate = date,
                        ScheduledTime = time,
                        MedName = med.Name,
                        DoseDisplay = $"{med.Dosage} {med.Unit}",
                        CanToggle = date < now.Date || (date == now.Date && time <= now.TimeOfDay),
                        Status = log?.Status,
                        ResolvedAt = log?.ResolvedAt
                    });
                }
            }

            foreach (var item in items.OrderBy(i => i.ScheduledTime).ThenBy(i => i.MedName))
                DosesForSelectedDate.Add(item);
        }

        HasNoDoses = DosesForSelectedDate.Count == 0;
    }

    // ── Week activity dots (visible week) ────────────────────────────────
    /// <summary>
    /// Flat, event-based list of indicators the calendar control renders as dots.
    /// Recomputed for the active pet whenever the visible week, pet, or a dose
    /// outcome changes. The control groups these by date and chooses dot styling.
    /// </summary>
    public ObservableCollection<CalendarActivity> WeekActivities { get; } = new();

    /// <summary>
    /// Build the visible week's activity indicators for the active pet:
    /// medication doses (scheduled → hollow, taken/skipped → filled) plus weight
    /// and mood entries (filled). Mirrors <see cref="LoadDosesAsync"/>'s rule
    /// expansion but across the seven days of the selected week.
    /// </summary>
    public async Task LoadWeekActivitiesAsync()
    {
        var petId = CurrentPetId;
        var weekStart = StartOfWeek(CurrentSelectedDate);
        var weekEnd = weekStart.AddDays(6);
        var windowEnd = weekEnd.AddDays(1).AddTicks(-1);

        var activities = new List<CalendarActivity>();

        if (petId != 0)
        {
            var meds = (await _medicationService.GetMedicationsByPetIdAsync(petId))
                .Where(m => !m.IsArchived)
                .ToList();

            foreach (var med in meds)
            {
                var schedules = await _medicationService.GetMedicationSchedulesByMedicationIdAsync(med.Id);
                if (schedules.Count == 0)
                    continue;

                var logs = await _doseLogService.GetByMedicationAndRangeAsync(med.Id, weekStart, weekEnd);
                // Never show doses for dates before the medication existed.
                var expandFrom = med.CreatedAt > weekStart ? med.CreatedAt : weekStart.AddTicks(-1);

                foreach (var schedule in schedules)
                {
                    foreach (var occurrence in Animal_Diary_App.Data.Services.Notifications.MedicationScheduleExpander.Expand(
                                 schedule.Day, schedule.Time, expandFrom, windowEnd))
                    {
                        var date = occurrence.Date;
                        var log = logs.FirstOrDefault(l => l.ScheduledDate == date && l.ScheduledTime == schedule.Time);
                        var state = log?.Status is DoseStatus.Taken or DoseStatus.Skipped
                            ? CalendarActivityState.Completed
                            : CalendarActivityState.Scheduled;

                        activities.Add(new CalendarActivity
                        {
                            Date = date,
                            Type = CalendarActivityType.Medication,
                            State = state
                        });
                    }
                }
            }

            var entries = await _petEntryService.GetPetEntriesByPetIdAndRangeAsync(petId, weekStart, weekEnd);
            foreach (var entry in entries)
            {
                if (entry.Weight > 0)
                    activities.Add(new CalendarActivity { Date = entry.Date.Date, Type = CalendarActivityType.Weight, State = CalendarActivityState.Completed });
                if (entry.MoodLevel > 0)
                    activities.Add(new CalendarActivity { Date = entry.Date.Date, Type = CalendarActivityType.Mood, State = CalendarActivityState.Completed });
            }
        }

        WeekActivities.Clear();
        foreach (var activity in activities)
            WeekActivities.Add(activity);
    }

    /// <summary>One-tap confirmation: toggle a dose between Taken and not-recorded.</summary>
    public ICommand ToggleDoseTakenCommand => new Command<DoseItem>(async item =>
    {
        if (item == null || !item.CanToggle)
            return;

        if (item.Status == DoseStatus.Taken)
        {
            await _doseLogService.ClearStatusAsync(item.MedicationId, item.ScheduledDate, item.ScheduledTime);
            item.ResolvedAt = null;
            item.Status = null;
        }
        else
        {
            await _doseLogService.SetStatusAsync(item.MedicationId, item.PetId, item.ScheduledDate, item.ScheduledTime, DoseStatus.Taken);
            item.ResolvedAt = DateTime.Now;
            item.Status = DoseStatus.Taken;
            // Stop this occurrence's reminder from firing late or being re-sent.
            await _reminderScheduler.MarkDoseHandledAsync(item.MedicationId, item.ScheduledDate, item.ScheduledTime);
        }

        // Reflect the new outcome in the month dots (hollow ↔ filled).
        await LoadWeekActivitiesAsync();
    });

    /// <summary>Secondary action: toggle a dose between Skipped and not-recorded.</summary>
    public ICommand SkipDoseCommand => new Command<DoseItem>(async item =>
    {
        if (item == null || !item.CanToggle)
            return;

        if (item.Status == DoseStatus.Skipped)
        {
            await _doseLogService.ClearStatusAsync(item.MedicationId, item.ScheduledDate, item.ScheduledTime);
            item.ResolvedAt = null;
            item.Status = null;
        }
        else
        {
            await _doseLogService.SetStatusAsync(item.MedicationId, item.PetId, item.ScheduledDate, item.ScheduledTime, DoseStatus.Skipped);
            item.ResolvedAt = DateTime.Now;
            item.Status = DoseStatus.Skipped;
            // A skipped dose is handled too — don't let its reminder nag.
            await _reminderScheduler.MarkDoseHandledAsync(item.MedicationId, item.ScheduledDate, item.ScheduledTime);
        }

        // Reflect the new outcome in the month dots (hollow ↔ filled).
        await LoadWeekActivitiesAsync();
    });

    public EntrySection MoodSection { get; } = new();
    public EntrySection WeightSection { get; } = new();
    public EntrySection MedicationSection { get; } = new();


    /// <summary>
    /// Loads pets and entries. Call from Main while that page is visible so Calendar opens ready.
    /// </summary>
    public async Task PrepareDataAsync()
    {
        ResetInputSections();
        await LoadPetsAsync();
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
    }

    /// <summary>
    /// Light refresh when returning to Calendar (entries only; pets already loaded).
    /// </summary>
    public async Task RefreshEntriesAsync()
    {
        ResetInputSections();
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
    }

    private void ResetInputSections()
    {
        EntrySection.HideInput(MoodSection);
        EntrySection.HideInput(WeightSection);
        EntrySection.HideInput(MedicationSection);
    }
    public ICommand ShowMoodInputCommand => new Command(() =>
    {
        EntrySection.ShowInput(MoodSection, WeightSection, MedicationSection);
    });
    public ICommand OnMoodEntryCompleted =>
    new Command(async () =>
    {
        EntrySection.HideInput(MoodSection);
        await SavePetMoodEntryAsync();
        await LoadEntriesAsync();
        SelectedMoodLevel = MoodLevel.None;
    });

    public ICommand ShowWeightInputCommand => new Command(() =>
    {
        EntrySection.ShowInput(WeightSection, MoodSection, MedicationSection);
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
        EntrySection.ShowInput(MedicationSection, MoodSection, WeightSection);
    });

    public int CurrentPetId
    {
        get => _activePetService.ActivePet?.Id ?? 0;
        private set
        {
            if (CurrentPetId != value)
            {
                var pet = Pets.FirstOrDefault(p => p.Id == value);
                if (pet != null)
                {
                    _activePetService.ActivePet = pet;
                    OnPropertyChanged();
                }
            }
        }
    }

    public ICommand SelectMoodCommand => new Command<string>(mood =>
    {
        if (int.TryParse(mood, out var moodValue))
        {
            SelectedMoodLevel = (MoodLevel)moodValue;
        }
    });

    public ICommand SelectPetCommand => new Command<Pet>(async pet =>
    {
        foreach (var p in Pets)
        {
            p.IsSelected = false;
        }
        pet.IsSelected = true;
        _activePetService.ActivePet = pet;
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
        Console.WriteLine($"Selected pet: {pet.Name}");
    });
}

