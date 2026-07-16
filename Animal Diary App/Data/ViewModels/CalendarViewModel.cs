namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;
using System.Windows.Input;
using System.Collections.ObjectModel;

/// <summary>
/// The Journal's week strip, pet selector and derived day headings — plus the
/// day's dose list and the HasMood/HasWeight flags, which the TODAY page's care
/// ring and next-med card consume from code-behind (MainPage.xaml.cs). The
/// logging surface itself lives in <see cref="JournalLogViewModel"/> and the
/// sheet VMs; the legacy inline mood/weight editors were removed with them.
/// </summary>
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
            NotifyDerived();
            LoadEntriesAsync().Forget();
            LoadDosesAsync().Forget();
            if (weekChanged)
                LoadWeekActivitiesAsync().Forget();
        }
    }

    /// <summary>Monday on/before the given date (week strips start on Monday).</summary>
    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-offset);
    }

    // The day's recorded mood/weight, kept only as the source of HasMood/HasWeight
    // (the Today page's care ring reads those from code-behind).
    private string shownMood = string.Empty;
    public string ShownMood
    {
        get => shownMood;
        set
        {
            if (SetProperty(ref shownMood, value))
                OnPropertyChanged(nameof(HasMood));
        }
    }

    private string shownWeight = string.Empty;
    public string ShownWeight
    {
        get => shownWeight;
        set
        {
            if (SetProperty(ref shownWeight, value))
                OnPropertyChanged(nameof(HasWeight));
        }
    }

    public bool HasMood => !string.IsNullOrEmpty(ShownMood);
    public bool HasWeight => !string.IsNullOrEmpty(ShownWeight);

    // ── Rockpool Journal display helpers (derived, read-only) ────────────
    // Refreshed together via NotifyDerived() whenever the selection, active
    // pet, or the day's entries/doses change.

    /// <summary>Active pet's name, or empty when none is selected.</summary>
    public string ActivePetName => _activePetService.ActivePet?.Name ?? string.Empty;

    /// <summary>Eyebrow: "{Pet}'s health journal".</summary>
    public string EyebrowText =>
        LocalizationManager.Instance.Format("Journal_EyebrowFmt", ActivePetName);

    /// <summary>True when the selected date is today.</summary>
    public bool IsSelectedDateToday => CurrentSelectedDate.Date == DateTime.Now.Date;

    /// <summary>Handwriting relative label: today / yesterday / N days ago / upcoming.</summary>
    public string RelativeDateLabel
    {
        get
        {
            var loc = LocalizationManager.Instance;
            int diff = (DateTime.Now.Date - CurrentSelectedDate.Date).Days;
            return diff switch
            {
                0 => loc.GetString("Journal_RelToday"),
                1 => loc.GetString("Journal_RelYesterday"),
                > 1 => loc.Format("Journal_RelDaysAgo", diff),
                _ => loc.GetString("Journal_RelUpcoming"),
            };
        }
    }

    /// <summary>Italic-serif day heading: "Today, so far…" or "How {Weekday} went".</summary>
    public string DayHeading =>
        IsSelectedDateToday
            ? LocalizationManager.Instance.GetString("Journal_HeadingToday")
            : LocalizationManager.Instance.Format(
                "Journal_HeadingPast",
                CurrentSelectedDate.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture));

    /// <summary>Empty-state headline: "A quiet page for {Pet}".</summary>
    public string EmptyHeadline =>
        LocalizationManager.Instance.Format("Journal_EmptyBig", ActivePetName);

    /// <summary>Today's mood, weight and every scheduled dose are all recorded.
    /// Consumed by the Today page's care ring (code-behind).</summary>
    public bool AllCareComplete =>
        IsSelectedDateToday && HasMood && HasWeight
        && DosesForSelectedDate.Count > 0
        && DosesForSelectedDate.All(d => d.Status == DoseStatus.Taken);

    /// <summary>Fire change notifications for every derived Journal label at once.
    /// NOTE: this raises ActivePetName on every entries/doses load, not only on a
    /// real pet switch — the CalendarPage dedupes its Journal reloads on that.</summary>
    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(ActivePetName));
        OnPropertyChanged(nameof(EyebrowText));
        OnPropertyChanged(nameof(IsSelectedDateToday));
        OnPropertyChanged(nameof(RelativeDateLabel));
        OnPropertyChanged(nameof(DayHeading));
        OnPropertyChanged(nameof(EmptyHeadline));
        OnPropertyChanged(nameof(AllCareComplete));
    }

    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly MedicationService _medicationService;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly DayDoseService _dayDoseService;
    private readonly Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler _reminderScheduler;

    public CalendarViewModel(
    PetEntryService petEntryService,
    PetService petService, ActivePetService activePetService,
    MedicationService medicationService, MedicationDoseLogService doseLogService,
    DayDoseService dayDoseService,
    Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler reminderScheduler)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _medicationService = medicationService;
        _doseLogService = doseLogService;
        _dayDoseService = dayDoseService;
        _reminderScheduler = reminderScheduler;

        // Commands are created once — an expression-bodied `=> new Command(...)`
        // property hands out a fresh instance per read.
        SelectPetCommand = new Command<Pet>(async pet => await SelectPetAsync(pet));
        ToggleDoseTakenCommand = new Command<DoseItem>(async item => await ToggleDoseTakenAsync(item));
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

        // Prefer the in-memory active pet: the saved id is written fire-and-forget
        // by ActivePetService, so reading it right after a switch on another tab
        // could race and stomp the user's selection back to the previous pet.
        var currentId = _activePetService.ActivePet?.Id ?? 0;
        var savedPetId = await _activePetService.GetSavedActivePetIdAsync();
        var selected = Pets.FirstOrDefault(p => p.Id == currentId)
            ?? Pets.FirstOrDefault(p => p.Id == savedPetId)
            ?? petsFromDb[0];
        _activePetService.ActivePet = selected;
        selected.IsSelected = true;
    }

    /// <summary>Load the selected day's mood/weight so HasMood/HasWeight (the Today
    /// page's care ring inputs) reflect the stored entry.</summary>
    public async Task LoadEntriesAsync()
    {
        var entry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(CurrentSelectedDate, CurrentPetId);
        if (entry == null)
        {
            ShownMood = string.Empty;
            ShownWeight = string.Empty;
            NotifyDerived();
            return;
        }
        // Derive the displayed mood from the stored level so it always renders in
        // the active language (the stored Mood string may be in another language).
        ShownMood = entry.MoodLevel > 0 ? ((MoodLevel)entry.MoodLevel).GetDisplayName() : string.Empty;
        ShownWeight = entry.Weight > 0 ? entry.Weight.ToString() : string.Empty;
        NotifyDerived();
    }

    // ── Medication dose checklist (selected date) ────────────────────────
    // Consumed by the Today page's care ring + next-med card (code-behind).
    public RangeObservableCollection<DoseItem> DosesForSelectedDate { get; } = new();

    /// <summary>
    /// Build the list of scheduled doses for the active pet on the selected date
    /// by filtering each medication's schedule rules to this weekday, then
    /// attaching any recorded outcome from the dose log.
    /// </summary>
    public async Task LoadDosesAsync()
    {
        var petId = CurrentPetId;
        var date = CurrentSelectedDate.Date;
        var ordered = new List<DoseItem>();

        if (petId != 0)
        {
            var now = DateTime.Now;

            // The day's doses come from the shared DayDoseService (same meds →
            // schedules → logs join the Journal timeline + pending engine use).
            var items = new List<DoseItem>();
            foreach (var d in await _dayDoseService.GetForDayAsync(petId, date))
            {
                var med = d.Medication;
                items.Add(new DoseItem
                {
                    MedicationId = med.Id,
                    PetId = petId,
                    ScheduledDate = date,
                    ScheduledTime = d.ScheduledTime,
                    MedName = med.Name,
                    DoseDisplay = $"{med.Dosage} {med.Unit}",
                    CanToggle = date < now.Date || (date == now.Date && d.ScheduledTime <= now.TimeOfDay),
                    Status = d.Log?.Status,
                    ResolvedAt = d.Log?.ResolvedAt
                });
            }

            // A "handmade" wobble: alternate the pill-icon tilt and cycle the
            // card corners through three patterns as they go down the timeline.
            int index = 0;
            foreach (var item in items.OrderBy(i => i.ScheduledTime).ThenBy(i => i.MedName))
            {
                item.IconRotation = index % 2 == 0 ? -3 : 2.5;
                item.CardCorner = (index % 3) switch
                {
                    0 => new CornerRadius(17, 14, 15, 18),
                    1 => new CornerRadius(14, 18, 17, 15),
                    _ => new CornerRadius(16, 15, 18, 14),
                };
                ordered.Add(item);
                index++;
            }
        }

        // One Reset notification for the whole checklist instead of Clear + N Adds.
        DosesForSelectedDate.ReplaceAll(ordered);
        NotifyDerived();
    }

    // ── Week activity dots (visible week) ────────────────────────────────
    /// <summary>
    /// Flat, event-based list of indicators the calendar control renders as dots.
    /// Recomputed for the active pet whenever the visible week, pet, or a dose
    /// outcome changes. The control groups these by date and chooses dot styling.
    /// </summary>
    public RangeObservableCollection<CalendarActivity> WeekActivities { get; } = new();

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

            // Two batched queries for the whole week instead of two per medication.
            var medIds = meds.Select(m => m.Id).ToList();
            var schedulesByMed = (await _medicationService.GetSchedulesForMedicationsAsync(medIds))
                .ToLookup(s => s.MedicationId);
            var logsByMed = (await _doseLogService.GetByMedicationsAndRangeAsync(medIds, weekStart, weekEnd))
                .ToLookup(l => l.MedicationId);

            foreach (var med in meds)
            {
                var schedules = schedulesByMed[med.Id].ToList();
                if (schedules.Count == 0)
                    continue;

                var logs = logsByMed[med.Id];
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

        WeekActivities.ReplaceAll(activities);
    }

    /// <summary>One-tap confirmation: toggle a dose between Taken and not-recorded.
    /// Used by the Today page's next-med card.</summary>
    public ICommand ToggleDoseTakenCommand { get; }

    private async Task ToggleDoseTakenAsync(DoseItem? item)
    {
        if (item == null || !item.CanToggle)
            return;

        if (item.Status == DoseStatus.Taken)
        {
            await _doseLogService.ClearStatusAsync(item.MedicationId, item.ScheduledDate, item.ScheduledTime);
            item.ResolvedAt = null;
            item.Status = null;
            // Marking it taken cancelled the occurrence's reminder; un-marking must
            // re-arm it or the dose would go silently unreminded. Idempotent sync.
            await _reminderScheduler.SyncMedicationAsync(item.MedicationId);
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
    }

    /// <summary>
    /// Loads pets and entries. Call from Main while that page is visible so Calendar opens ready.
    /// </summary>
    public async Task PrepareDataAsync()
    {
        await LoadPetsAsync();
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
    }

    /// <summary>
    /// Light refresh when returning to Calendar (entries only; pets already loaded).
    /// </summary>
    public async Task RefreshEntriesAsync()
    {
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
    }

    public int CurrentPetId => _activePetService.ActivePet?.Id ?? 0;

    public ICommand SelectPetCommand { get; }

    private async Task SelectPetAsync(Pet pet)
    {
        if (pet == null)
            return;

        foreach (var p in Pets)
        {
            p.IsSelected = false;
        }
        pet.IsSelected = true;
        _activePetService.ActivePet = pet;
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadWeekActivitiesAsync());
        NotifyDerived();
    }
}
