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
            NotifyDerived();
            _ = LoadEntriesAsync();
            _ = LoadDosesAsync();
            _ = LoadHubAsync();
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

    private string shownMoodNote = string.Empty;
    /// <summary>The owner's free-text note saved with today's mood (empty if none).
    /// Shown on the timeline's washi-tape card in place of the generic narrative.</summary>
    public string ShownMoodNote
    {
        get => shownMoodNote;
        set { if (SetProperty(ref shownMoodNote, value)) OnPropertyChanged(nameof(MoodNarrative)); }
    }

    /// <summary>Localized "Selected Date: …" label for the calendar.</summary>
    public string SelectedDateDisplay =>
        LocalizationManager.Instance.Format("Calendar_SelectedDate", CurrentSelectedDate);

    // ── Rockpool Journal display helpers (additive, read-only) ───────────
    // Small derived labels the warm reskin needs; none change the shape of an
    // existing member. Refreshed together via NotifyDerived() whenever the
    // selection, active pet, or the day's entries/doses change.

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

    /// <summary>Warm-paper note line. Prefers the owner's own mood note; falls back
    /// to the generated "{Pet} was feeling {mood}" line when they didn't write one.</summary>
    public string MoodNarrative =>
        !string.IsNullOrWhiteSpace(ShownMoodNote)
            ? ShownMoodNote
            : HasMood
                ? LocalizationManager.Instance.Format("Journal_MoodNarrative", ActivePetName, ShownMood)
                : string.Empty;

    /// <summary>Empty-state headline: "A quiet page for {Pet}".</summary>
    public string EmptyHeadline =>
        LocalizationManager.Instance.Format("Journal_EmptyBig", ActivePetName);

    /// <summary>Celebration line shown when the day's whole care routine is done.</summary>
    public string CelebrationText =>
        LocalizationManager.Instance.Format("Journal_Celebrate", ActivePetName);

    /// <summary>Nothing logged for the selected day → show the quiet-page empty state.
    /// The mood/weight leaves cover mood + weight, so we only add the other leaves.</summary>
    public bool IsDayEmpty =>
        DosesForSelectedDate.Count == 0 && !_allLeaves.Any(l => l.HasValue);

    /// <summary>Today's mood, weight and every scheduled dose are all recorded.</summary>
    public bool AllCareComplete =>
        IsSelectedDateToday && HasMood && HasWeight
        && DosesForSelectedDate.Count > 0
        && DosesForSelectedDate.All(d => d.Status == DoseStatus.Taken);

    /// <summary>Fire change notifications for every derived Journal label at once.</summary>
    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(ActivePetName));
        OnPropertyChanged(nameof(EyebrowText));
        OnPropertyChanged(nameof(IsSelectedDateToday));
        OnPropertyChanged(nameof(RelativeDateLabel));
        OnPropertyChanged(nameof(DayHeading));
        OnPropertyChanged(nameof(MoodNarrative));
        OnPropertyChanged(nameof(ConditionName));
        OnPropertyChanged(nameof(EmptyHeadline));
        OnPropertyChanged(nameof(CelebrationText));
        OnPropertyChanged(nameof(IsDayEmpty));
        OnPropertyChanged(nameof(AllCareComplete));
    }

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
    private readonly TrackingEntryService _trackingService;
    private readonly Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler _reminderScheduler;
    public MedicationViewModel MedicationVM { get; }

    public CalendarViewModel(
    PetEntryService petEntryService,
    MedicationViewModel medicationVM, PetService petService, ActivePetService activePetService,
    MedicationService medicationService, MedicationDoseLogService doseLogService,
    TrackingEntryService trackingService,
    Animal_Diary_App.Data.Services.Notifications.MedicationReminderScheduler reminderScheduler)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _medicationService = medicationService;
        _doseLogService = doseLogService;
        _trackingService = trackingService;
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
            ShownMoodNote = string.Empty;
            ShownWeight = string.Empty;
            SelectedMoodLevel = MoodLevel.None;
            NotifyDerived();
            return;
        }
        // Derive the displayed mood from the stored level so it always renders in
        // the active language (the stored Mood string may be in another language).
        ShownMood = Entries.MoodLevel > 0 ? ((MoodLevel)Entries.MoodLevel).GetDisplayName() : string.Empty;
        ShownMoodNote = Entries.MoodNote ?? string.Empty;
        ShownWeight = Entries.Weight > 0 ? Entries.Weight.ToString() : string.Empty;
        SelectedMoodLevel = (MoodLevel)Entries.MoodLevel;
        NotifyDerived();
        return;
    }

    // ── Medication dose checklist (selected date) ────────────────────────
    public RangeObservableCollection<DoseItem> DosesForSelectedDate { get; } = new();

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
        var petId = CurrentPetId;
        var date = CurrentSelectedDate.Date;
        var ordered = new List<DoseItem>();

        if (petId != 0)
        {
            var weekday = date.DayOfWeek;
            var now = DateTime.Now;

            var meds = (await _medicationService.GetMedicationsByPetIdAsync(petId))
                .Where(m => !m.IsArchived)
                .ToList();
            var logs = await _doseLogService.GetByPetAndDateAsync(petId, date);

            // Fetch every medication's schedules in one query instead of one
            // round-trip per med, then group by medication id.
            var schedulesByMed = (await _medicationService.GetSchedulesForMedicationsAsync(
                    meds.Select(m => m.Id).ToList()))
                .ToLookup(s => s.MedicationId);

            var items = new List<DoseItem>();
            foreach (var med in meds)
            {
                var times = schedulesByMed[med.Id].Where(s => s.Day == weekday).Select(s => s.Time).Distinct();

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
        HasNoDoses = ordered.Count == 0;
        RefreshGroupSummaries();
        OnPropertyChanged(nameof(ShowMedsEmpty));
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

        // Reflect the new outcome in the month dots (hollow ↔ filled) and the
        // Medications group's rollup (e.g. 2/3).
        await LoadWeekActivitiesAsync();
        RefreshGroupSummaries();
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

        // Reflect the new outcome in the month dots (hollow ↔ filled) and the
        // Medications group's rollup (e.g. 2/3).
        await LoadWeekActivitiesAsync();
        RefreshGroupSummaries();
    });

    // ── Tracker Hub (progressive-disclosure logging) ─────────────────────
    // Every tracker — Mood, Weight, Appetite, a medication dose, a condition
    // reading — is surfaced through one flow: a short chooser (RootRows) →
    // optional group drill-in (CurrentGroup) → a single tracker's input
    // (OpenLeaf). See TrackerHub.cs for the pieces.

    /// <summary>Chooser rows shown at the hub root.</summary>
    public ObservableCollection<TrackerRow> RootRows { get; } = new();

    /// <summary>Every leaf currently built (across root + groups), for the
    /// "is the day empty?" check and summary refreshes.</summary>
    private readonly List<TrackerLeaf> _allLeaves = new();

    private TrackerGroup? _medsGroup;      // the Medications group (dose checklist)
    private TrackerGroup? _conditionGroup; // the active pet's condition group, if any

    private TrackerGroup? _currentGroup;
    /// <summary>The group the user has drilled into (null = root).</summary>
    public TrackerGroup? CurrentGroup
    {
        get => _currentGroup;
        private set { if (SetProperty(ref _currentGroup, value)) NotifyHub(); }
    }

    private TrackerLeaf? _openLeaf;
    /// <summary>The tracker whose input is currently disclosed (null = none).</summary>
    public TrackerLeaf? OpenLeaf
    {
        get => _openLeaf;
        private set { if (SetProperty(ref _openLeaf, value)) NotifyHub(); }
    }

    /// <summary>Rows for the current level (a group's children, or the root).</summary>
    public IReadOnlyList<TrackerRow> VisibleRows =>
        (IReadOnlyList<TrackerRow>?)CurrentGroup?.Rows ?? RootRows;

    /// <summary>The one open leaf wrapped as a 0/1-item source, so a BindableLayout
    /// + template selector can render its input.</summary>
    public IEnumerable<TrackerLeaf> OpenLeafItems =>
        OpenLeaf is null ? Array.Empty<TrackerLeaf>() : new[] { OpenLeaf };

    public string HubTitle => OpenLeaf?.Name ?? CurrentGroup?.Name ?? "Today's Tracking";
    public bool ShowBack => OpenLeaf != null || CurrentGroup != null;
    public bool ShowRows => OpenLeaf == null && CurrentGroup?.IsMedications != true;
    public bool ShowMedications => OpenLeaf == null && CurrentGroup?.IsMedications == true;
    /// <summary>Medications group is open but nothing is scheduled today.</summary>
    public bool ShowMedsEmpty => ShowMedications && HasNoDoses;
    public bool HasHub => RootRows.Count > 0;

    /// <summary>The active pet's condition name (used elsewhere too).</summary>
    public string ConditionName =>
        ConditionCatalog.GetCondition(_activePetService.ActivePet?.ConditionId).Name;

    /// <summary>Raised after any tracker is saved (with a short confirmation), so
    /// the page can show a gentle toast.</summary>
    public event Action<string>? TrackingChanged;

    /// <summary>Tap a chooser row: drill into a group, or open a leaf's input.</summary>
    public ICommand SelectRowCommand => new Command<TrackerRow>(row =>
    {
        if (row == null)
            return;

        if (row.Group != null)
        {
            CurrentGroup = row.Group;
            OpenLeaf = null;
        }
        else if (row.Leaf != null)
        {
            OpenLeaf = row.Leaf;
        }
    });

    /// <summary>Back: close an open leaf, else leave the current group.</summary>
    public ICommand HubBackCommand => new Command(() =>
    {
        if (OpenLeaf != null)
            OpenLeaf = null;
        else if (CurrentGroup != null)
            CurrentGroup = null;
    });

    /// <summary>
    /// (Re)build the whole hub for the active pet + selected date: the native Mood
    /// and Weight leaves, the general Appetite leaf, the Medications group and the
    /// pet's condition group. Every leaf is hydrated with the day's current value
    /// so its chooser row shows a ✓ summary without being opened.
    /// </summary>
    public async Task LoadHubAsync()
    {
        // Detach old leaves and reset navigation to the root.
        foreach (var leaf in _allLeaves)
            leaf.Saved -= OnLeafSaved;
        _allLeaves.Clear();
        RootRows.Clear();
        _medsGroup = null;
        _conditionGroup = null;
        _currentGroup = null;
        _openLeaf = null;

        var petId = CurrentPetId;
        if (petId != 0)
        {
            var date = CurrentSelectedDate.Date;
            var petEntry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(date, petId);
            var tracking = (await _trackingService.GetForDateAsync(petId, date))
                .ToDictionary(e => e.ItemId);

            TrackingEntry? Saved(string id) => tracking.TryGetValue(id, out var e) ? e : null;

            // Native leaves (bespoke inputs, PetEntry storage).
            RootRows.Add(TrackLeaf(new MoodTrackerLeaf(petId, date, _petEntryService, petEntry)));
            RootRows.Add(TrackLeaf(new WeightTrackerLeaf(petId, date, _petEntryService, petEntry)));

            // General non-native leaves (Appetite) as their own root rows.
            foreach (var item in ConditionCatalog.GetGeneralDynamicItems())
                RootRows.Add(TrackLeaf(new DynamicTrackerLeaf(item, petId, date, _trackingService, Saved(item.Id))));

            // Medications group (its content is the dose checklist).
            _medsGroup = new TrackerGroup { Name = "Medications", Icon = "💊", IsMedications = true };
            RootRows.Add(new TrackerRow(_medsGroup));

            // Condition group (e.g. Diabetes → Insulin, Blood Glucose, Water Intake).
            var conditionId = _activePetService.ActivePet?.ConditionId ?? string.Empty;
            var conditionItems = ConditionCatalog.GetConditionItems(conditionId);
            if (conditionItems.Count > 0)
            {
                var condition = ConditionCatalog.GetCondition(conditionId);
                _conditionGroup = new TrackerGroup { Name = condition.Name, Icon = condition.Icon };
                foreach (var item in conditionItems)
                {
                    var leaf = new DynamicTrackerLeaf(item, petId, date, _trackingService, Saved(item.Id));
                    leaf.Saved += OnLeafSaved;
                    _allLeaves.Add(leaf);
                    _conditionGroup.Rows.Add(new TrackerRow(leaf));
                }
                RootRows.Add(new TrackerRow(_conditionGroup));
            }
        }

        RefreshGroupSummaries();
        NotifyHub();
        OnPropertyChanged(nameof(ConditionName));
        OnPropertyChanged(nameof(IsDayEmpty));
    }

    /// <summary>Wire a leaf up (save handler + empty-day tracking) and wrap it in a row.</summary>
    private TrackerRow TrackLeaf(TrackerLeaf leaf)
    {
        leaf.Saved += OnLeafSaved;
        _allLeaves.Add(leaf);
        return new TrackerRow(leaf);
    }

    private async void OnLeafSaved(TrackerLeaf leaf)
    {
        // Collapse the input back to the chooser and refresh what changed.
        OpenLeaf = null;
        await LoadEntriesAsync();      // refresh mood/weight timeline cards
        RefreshGroupSummaries();
        OnPropertyChanged(nameof(IsDayEmpty));
        TrackingChanged?.Invoke($"{leaf.Name} noted 💛");
    }

    /// <summary>Recompute the Medications and condition group rollups.</summary>
    private void RefreshGroupSummaries()
    {
        if (_medsGroup != null)
        {
            var total = DosesForSelectedDate.Count;
            var taken = DosesForSelectedDate.Count(d => d.Status == DoseStatus.Taken);
            _medsGroup.Summary = total == 0 ? string.Empty : $"{taken}/{total}";
        }

        if (_conditionGroup != null)
        {
            var logged = _conditionGroup.Rows.Count(r => r.Leaf?.HasValue == true);
            _conditionGroup.Summary = logged == 0 ? string.Empty : $"{logged} logged";
        }
    }

    /// <summary>Notify every derived hub-navigation property at once.</summary>
    private void NotifyHub()
    {
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(OpenLeafItems));
        OnPropertyChanged(nameof(HubTitle));
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(ShowRows));
        OnPropertyChanged(nameof(ShowMedications));
        OnPropertyChanged(nameof(ShowMedsEmpty));
        OnPropertyChanged(nameof(HasHub));
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
        await LoadPetsAsync();
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadHubAsync(), LoadWeekActivitiesAsync());
        RefreshGroupSummaries();
    }

    /// <summary>
    /// Light refresh when returning to Calendar (entries only; pets already loaded).
    /// </summary>
    public async Task RefreshEntriesAsync()
    {
        ResetInputSections();
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadHubAsync(), LoadWeekActivitiesAsync());
        RefreshGroupSummaries();
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
        await Task.WhenAll(LoadEntriesAsync(), LoadDosesAsync(), LoadHubAsync(), LoadWeekActivitiesAsync());
        RefreshGroupSummaries();
        NotifyDerived();
    });
}

