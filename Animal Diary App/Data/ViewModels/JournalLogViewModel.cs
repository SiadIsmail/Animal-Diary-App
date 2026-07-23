namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;

// ─────────────────────────────────────────────────────────────────────────────
//  The Journal's "Still to do" chip row + the glucose/appetite timeline entries.
//
//  All NEW functionality — the pending list, one-tap dose logging with undo, care-
//  plan access — so it lives here rather than reshaping the CalendarViewModel. The
//  page coordinates the sheets + animations (bubble-pop, toast); this VM owns the
//  data and the medical actions. The word "Diabetes" never appears here: it only
//  knows trackers and med doses.
// ─────────────────────────────────────────────────────────────────────────────

public enum JournalChipKind { Medication, Glucose, Mood, Appetite, Weight, Seizure, Water, Add }

/// <summary>One "Still to do" chip.</summary>
public class JournalChip
{
    public JournalChipKind Kind { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    /// <summary>Trailing detail: a dose time ("18:00") or a "1 of 3" count.</summary>
    public string Detail { get; init; } = string.Empty;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    /// <summary>Medication chips wear the honey accent.</summary>
    public bool IsMedication { get; init; }

    /// <summary>Slight alternating tilt (±0.4°) — imperfection on the frame only.</summary>
    public double Tilt { get; init; }

    /// <summary>Screen-reader label, e.g. "Log glucose, 1 of 3 done today".</summary>
    public string SemanticLabel { get; init; } = string.Empty;

    // Medication identity, for one-tap logging.
    public int MedicationId { get; init; }
    public int PetId { get; init; }
    public TimeSpan DoseTime { get; init; }
}

/// <summary>Which kind of entry a <see cref="TimelineItem"/> represents. Drives only
/// the template pick (Mood gets the washi-note card, everything else the standard
/// card) — never the ordering, which is purely by time.</summary>
public enum TimelineKind { Mood, Weight, Glucose, Appetite, AppetiteAmount, Seizure, WaterAmount, WaterLevel, Dose }

/// <summary>One entry on the Journal's single chronological timeline, whatever its
/// kind. Everything logged for the day — mood, weight, glucose, appetite, seizures
/// and medication doses — becomes one of these and is sorted purely by
/// <see cref="Time"/>. <see cref="Time"/> is null only for legacy mood/weight rows
/// saved before per-entry times existed; those sort at the start of the day and
/// hide their time label.</summary>
public class TimelineItem
{
    public TimelineKind Kind { get; init; }

    /// <summary>The database row id this entry deletes, for the per-row stores
    /// (glucose / appetite / seizure). 0 for mood/weight (keyed by pet+date, cleared
    /// on their shared PetEntry) and for doses (not a deletable log).</summary>
    public int EntryId { get; init; }

    /// <summary>Whether this entry offers a ✕ affordance. True for every logged
    /// reading; for a dose it means "an outcome is recorded" — the ✕ then clears that
    /// outcome (a still-open dose has nothing to clear and shows none).</summary>
    public bool CanDelete { get; init; }

    /// <summary>Time of day the entry was recorded, or null for a legacy mood/weight
    /// row with no stored time.</summary>
    public TimeSpan? Time { get; init; }
    public string TimeDisplay => Time?.ToString(@"hh\:mm") ?? string.Empty;
    public bool HasTime => Time.HasValue;

    public string Icon { get; init; } = string.Empty;

    /// <summary>Icon-tile tint (resolved from the app's rockpool colour tokens).</summary>
    public Color Tint { get; init; } = Colors.Transparent;

    /// <summary>Slight alternating tilt down the timeline — imperfection on the frame.
    /// Assigned by the builder once the list is in its final chronological order.</summary>
    public double IconRotation { get; set; }

    public string Title { get; init; } = string.Empty;
    public string Sub { get; init; } = string.Empty;
    public bool HasSub => !string.IsNullOrEmpty(Sub);

    /// <summary>Mood only: the warm-paper washi note line.</summary>
    public string Note { get; init; } = string.Empty;

    // ── Dose-only action state (Kind == Dose) ──────────────────────────────────────
    // A dose card can clear its outcome (the shared ✕) or be marked skipped (its own
    // button). Both need the dose's identity (medication + time) and its current
    // outcome; non-dose kinds leave these at their defaults.
    /// <summary>Medication this dose belongs to — for the ✕ (clear) and skip actions.</summary>
    public int MedicationId { get; init; }

    /// <summary>The dose's scheduled time-of-day (its key, not its resolved time).</summary>
    public TimeSpan DoseTime { get; init; }

    /// <summary>The dose's recorded outcome, or null when it hasn't been acted on.</summary>
    public DoseStatus? DoseOutcome { get; init; }

    /// <summary>A past or already-due dose (never a future occurrence) — the only ones
    /// that can be skipped or cleared.</summary>
    public bool DoseActionable { get; init; }

    public bool IsDose => Kind == TimelineKind.Dose;

    /// <summary>Show the "Mark as skipped" button: an actionable dose not already skipped.</summary>
    public bool CanSkipDose => IsDose && DoseActionable && DoseOutcome != DoseStatus.Skipped;
}

/// <summary>A log type offered in the "+" (add-anything) sheet.</summary>
public class AddOption
{
    public JournalChipKind Kind { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public class JournalLogViewModel : BaseViewModel
{
    private readonly PendingItemsService _pending;
    private readonly CarePlanService _carePlan;
    private readonly ActivePetService _activePet;
    private readonly DayDoseService _dayDoses;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly MedicationReminderScheduler _reminders;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly SeizureEntryService _seizures;
    private readonly WaterEntryService _water;

    private DateTime _date = DateTime.Now.Date;

    public JournalLogViewModel(
        PendingItemsService pending,
        CarePlanService carePlan,
        ActivePetService activePet,
        DayDoseService dayDoses,
        MedicationDoseLogService doseLogs,
        MedicationReminderScheduler reminders,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        SeizureEntryService seizures,
        WaterEntryService water)
    {
        _pending = pending;
        _carePlan = carePlan;
        _activePet = activePet;
        _dayDoses = dayDoses;
        _doseLogs = doseLogs;
        _reminders = reminders;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _seizures = seizures;
        _water = water;

        OpenAddSheetCommand = new Command(async () => await OpenAddSheetAsync());
        CloseAddSheetCommand = new Command(() => IsAddSheetVisible = false);
        SelectAddOptionCommand = new Command<AddOption>(OnSelectAddOption);
        DeleteItemCommand = new Command<TimelineItem>(async i => await DeleteItemAsync(i));
        SkipDoseCommand = new Command<TimelineItem>(async i => await SkipDoseAsync(i));
    }

    // Short-hand for the localized string manager (usable from the static chip builders).
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Raised when the person picks a log type (from a chip or the "+"
    /// sheet). The page opens the matching sheet — it owns the sheet VMs + animations.</summary>
    public event Action<JournalChipKind>? RequestOpenSheet;

    /// <summary>Raised after a timeline entry is deleted, carrying the confirmation
    /// line + an undo that restores it — the page shows the standard undo-toast and
    /// refreshes (same safety net every destructive Journal action uses).</summary>
    public event Action<JournalSaveResult>? ItemDeleted;

    /// <summary>Delete one logged timeline entry (the ✕ on a card). Dispatches to the
    /// right store by kind; a dose's ✕ clears its recorded outcome instead.</summary>
    public ICommand DeleteItemCommand { get; }

    /// <summary>Mark a dose as skipped (the "Mark as skipped" button on a dose card).
    /// A skip is a first-class non-adherence fact that reaches the vet report.</summary>
    public ICommand SkipDoseCommand { get; }

    // ── Chip row ────────────────────────────────────────────────────────────────
    public ObservableCollection<JournalChip> Chips { get; } = new();

    private string _heading = string.Empty;
    public string Heading { get => _heading; private set => SetProperty(ref _heading, value); }

    private bool _isToday = true;
    public bool IsToday
    {
        get => _isToday;
        private set { if (SetProperty(ref _isToday, value)) NotifyStates(); }
    }

    private bool _hasPending;
    public bool HasPending
    {
        get => _hasPending;
        private set { if (SetProperty(ref _hasPending, value)) NotifyStates(); }
    }

    /// <summary>Today, and everything's done — show the paw celebration.</summary>
    public bool ShowAllDone => IsToday && !HasPending && _hasPet;

    /// <summary>Today, with things left — show the heading + chips.</summary>
    public bool ShowStillToDo => IsToday && HasPending;

    /// <summary>A past day — no pending logic, just a single "add" affordance.</summary>
    public bool ShowPastAdd => !IsToday && _hasPet;

    private bool _hasPet;

    public string CelebrationText => Loc.Format("Journal_Celebrate", PetName);

    private string _petName = string.Empty;
    public string PetName
    {
        get => _petName;
        private set { if (SetProperty(ref _petName, value)) OnPropertyChanged(nameof(CelebrationText)); }
    }

    // ── Timeline (one chronological list of everything logged for the day) ─────────
    // Mood, weight, glucose, appetite, seizures and medication doses all become
    // TimelineItems here and are sorted purely by time — a single ordering, no
    // per-kind sections. The page renders them with one template selector.
    public ObservableCollection<TimelineItem> TimelineItems { get; } = new();

    public bool HasTimelineItems => TimelineItems.Count > 0;

    /// <summary>Nothing logged for the day → the page shows its quiet-page empty state.</summary>
    public bool IsTimelineEmpty => _hasPet && TimelineItems.Count == 0;

    // ── Add-anything sheet ───────────────────────────────────────────────────────
    // A plain reassigned list rather than an in-place-mutated ObservableCollection:
    // assigning a fresh list raises one property change and the sheet's ItemsSource
    // binding rebuilds its rows from scratch.
    private IReadOnlyList<AddOption> _addOptions = System.Array.Empty<AddOption>();
    public IReadOnlyList<AddOption> AddOptions
    {
        get => _addOptions;
        private set => SetProperty(ref _addOptions, value);
    }

    private bool _isAddSheetVisible;
    public bool IsAddSheetVisible { get => _isAddSheetVisible; set => SetProperty(ref _isAddSheetVisible, value); }

    public string AddSheetTitle => Loc.GetString(IsToday ? "Journal_AddMoreTitle" : "Journal_AddDayTitle");
    public string AddSheetSubtitle => Loc.Format("Journal_AddSheetSub", PetName, _date);

    public ICommand OpenAddSheetCommand { get; }
    public ICommand CloseAddSheetCommand { get; }
    public ICommand SelectAddOptionCommand { get; }

    private void OnSelectAddOption(AddOption? option)
    {
        if (option == null)
            return;
        IsAddSheetVisible = false;
        RequestOpenSheet?.Invoke(option.Kind);
    }

    private async Task OpenAddSheetAsync()
    {
        await BuildAddOptionsAsync();
        OnPropertyChanged(nameof(AddSheetTitle));
        OnPropertyChanged(nameof(AddSheetSubtitle));
        IsAddSheetVisible = true;
    }

    // ── Load ─────────────────────────────────────────────────────────────────────
    public async Task ReloadAsync(DateTime date)
    {
        _date = date.Date;
        var pet = _activePet.ActivePet;
        _hasPet = pet != null && pet.Id != 0;
        PetName = pet?.Name ?? string.Empty;
        IsToday = _date == DateTime.Now.Date;

        // Gather everything (the awaits) BEFORE touching the observable collections.
        // Several reloads fire on startup (OnAppearing + the date/pet PropertyChanged
        // handlers); if we cleared before awaiting, their clear+add would interleave
        // and duplicate the rows. The fill below is await-free, so each reload rebuilds
        // atomically on the UI thread.
        var timeline = await GatherTimelineAsync(pet);

        IReadOnlyList<PendingItem> pending = System.Array.Empty<PendingItem>();
        if (_hasPet && IsToday)
            pending = await _pending.GetAsync(pet!, _date);

        // ── atomic fill: no awaits from here on ──
        TimelineItems.Clear();
        foreach (var t in timeline) TimelineItems.Add(t);
        RaiseTimelineFlags();

        Chips.Clear();
        if (_hasPet && IsToday)
        {
            BuildChips(pending);
            var count = Chips.Count; // real pending items, before the trailing "+"
            HasPending = count > 0;
            Heading = BuildHeading(count);
            if (HasPending)
                Chips.Add(new JournalChip
                {
                    Kind = JournalChipKind.Add,
                    Icon = "＋",
                    SemanticLabel = Loc.GetString("Journal_AddMoreTitle")
                });
        }
        else
        {
            HasPending = false;
        }

        NotifyStates();
    }

    private void BuildChips(IReadOnlyList<PendingItem> pending)
    {
        int i = 0;
        foreach (var item in pending)
        {
            var tilt = (i++ % 2 == 0) ? -0.4 : 0.4;
            Chips.Add(item.Kind == PendingKind.Medication
                ? MedChip(item, tilt)
                : TrackerChip(item, tilt));
        }
    }

    private static JournalChip MedChip(PendingItem item, double tilt)
    {
        var time = item.DoseTime?.ToString(@"hh\:mm") ?? string.Empty;
        return new JournalChip
        {
            Kind = JournalChipKind.Medication,
            Icon = "💊",
            Label = item.MedicationName,
            Detail = time,
            IsMedication = true,
            Tilt = tilt,
            SemanticLabel = Loc.Format("Journal_A11yGiveMed", item.MedicationName, time),
            MedicationId = item.MedicationId,
            PetId = item.PetId,
            DoseTime = item.DoseTime ?? TimeSpan.Zero
        };
    }

    private static JournalChip TrackerChip(PendingItem item, double tilt) => item.TrackerId switch
    {
        TrackerId.Glucose => new JournalChip
        {
            Kind = JournalChipKind.Glucose,
            Icon = "🩸",
            Label = Loc.GetString("Journal_GlucoseCheck"),
            Detail = Loc.Format("Journal_CountOfN", item.Done, item.Target),
            Tilt = tilt,
            SemanticLabel = Loc.Format("Journal_A11yLogGlucose", item.Done, item.Target)
        },
        TrackerId.Appetite => Simple(JournalChipKind.Appetite, "🍽️", Loc.GetString("Journal_Appetite"), tilt, Loc.GetString("Journal_A11yLogAppetite")),
        TrackerId.Weight => Simple(JournalChipKind.Weight, "⚖️", Loc.GetString("Journal_WeighIn"), tilt, Loc.GetString("Journal_A11yLogWeight")),
        TrackerId.Mood => Simple(JournalChipKind.Mood, "🙂", Loc.GetString("Journal_MoodTitle"), tilt, Loc.GetString("Journal_A11yLogMood")),
        TrackerId.Water => Simple(JournalChipKind.Water, "💧", Loc.GetString("Journal_Water"), tilt, Loc.GetString("Journal_A11yLogWater")),
        _ => Simple(JournalChipKind.Mood, "🙂", Loc.GetString("Journal_MoodTitle"), tilt, Loc.GetString("Journal_A11yLogMood"))
    };

    private static JournalChip Simple(JournalChipKind kind, string icon, string label, double tilt, string semantic) => new()
    {
        Kind = kind,
        Icon = icon,
        Label = label,
        Tilt = tilt,
        SemanticLabel = semantic
    };

    private static string BuildHeading(int n) =>
        Loc.Format(n == 1 ? "Journal_StillToDoOne" : "Journal_StillToDoMany", n);

    // Gather everything logged for the day as one list, then sort purely by time —
    // a single chronological ordering across every kind (§3). Legacy mood/weight rows
    // with no stored time sort at the start of the day.
    private async Task<List<TimelineItem>> GatherTimelineAsync(Pet? pet)
    {
        var items = new List<TimelineItem>();

        if (pet == null || pet.Id == 0)
            return items;

        // Mood + Weight (both live on the day's PetEntry, each with its own time).
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, pet.Id);
        if (entry != null)
        {
            if (entry.MoodLevel > 0)
            {
                var moodWord = ((MoodLevel)entry.MoodLevel).GetDisplayName();
                items.Add(new TimelineItem
                {
                    Kind = TimelineKind.Mood,
                    CanDelete = true,
                    Time = TicksToTime(entry.MoodTimeTicks),
                    Icon = "😊",
                    Tint = Tint("TealTint"),
                    Title = Loc.GetString("Journal_MoodTitle"),
                    Note = !string.IsNullOrWhiteSpace(entry.MoodNote)
                        ? entry.MoodNote
                        : Loc.Format("Journal_MoodNarrative", pet.Name, moodWord)
                });
            }

            if (entry.Weight > 0)
            {
                var weight = entry.Weight.ToString(CultureInfo.CurrentCulture)
                    + Loc.GetString("Common_KgSuffix");
                items.Add(new TimelineItem
                {
                    Kind = TimelineKind.Weight,
                    CanDelete = true,
                    Time = TicksToTime(entry.WeightTimeTicks),
                    Icon = "⚖️",
                    Tint = Tint("BlueTint"),
                    Title = Loc.GetString("Journal_WeighIn"),
                    Sub = $"{weight} · {Loc.GetString("Journal_WeightScales")}"
                });
            }
        }

        // Glucose (rose) — value is precise; range sentence only when a range exists.
        var range = (await _carePlan.GetPlanAsync(pet))
            .FirstOrDefault(t => t.TrackerId == TrackerId.Glucose)?.TargetRange;
        foreach (var g in await _glucose.GetForDateAsync(pet.Id, _date))
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Glucose,
                CanDelete = true,
                EntryId = g.Id,
                Time = g.Time,
                Icon = "🩸",
                Tint = Tint("RoseTint"),
                Title = Loc.Format("Journal_GlucoseTimeline", g.Value.ToString("0.0", CultureInfo.CurrentCulture)),
                Sub = GlucoseSub(g, range)
            });
        }

        // Appetite (honey) — two kinds that can both appear: the day's qualitative
        // reading (Didn't eat … everything) and any exact grams events. Both may carry
        // a food label. Never judged.
        foreach (var a in await _appetite.GetForDateAsync(pet.Id, _date))
        {
            var word = ((AppetiteLevel)a.Level).GetDisplayName().ToLowerInvariant();
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Appetite,
                CanDelete = true,
                EntryId = a.Id,
                Time = a.Time,
                Icon = "🍽️",
                Tint = Tint("HoneyWarmTint"),
                Title = Loc.GetString("Journal_Appetite"),
                Sub = WithFood(Loc.Format("Journal_AteWord", word), a.Food)
            });
        }
        foreach (var a in await _appetite.GetAmountsForDateAsync(pet.Id, _date))
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.AppetiteAmount,
                CanDelete = true,
                EntryId = a.Id,
                Time = a.Time,
                Icon = "🍽️",
                Tint = Tint("HoneyWarmTint"),
                Title = Loc.GetString("Journal_Appetite"),
                Sub = WithFood(Loc.Format("Journal_AppetiteGrams", a.Grams.ToString("0.#", CultureInfo.CurrentCulture)), a.Food)
            });
        }

        // Water (blue) — two independent kinds that can both appear on a day:
        //   • exact ml readings, one card each (additive events), and
        //   • the day's single relative reading (Barely … a lot).
        // Never judged; the value is a plain fact.
        foreach (var w in await _water.GetAmountsForDateAsync(pet.Id, _date))
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.WaterAmount,
                CanDelete = true,
                EntryId = w.Id,
                Time = w.Time,
                Icon = "💧",
                Tint = Tint("BlueTint"),
                Title = Loc.GetString("Journal_Water"),
                Sub = Loc.Format("Journal_WaterMl", w.AmountMl.ToString("0.#", CultureInfo.CurrentCulture))
            });
        }
        foreach (var w in await _water.GetLevelsForDateAsync(pet.Id, _date))
        {
            var word = ((WaterLevel)w.Level).GetDisplayName().ToLowerInvariant();
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.WaterLevel,
                CanDelete = true,
                EntryId = w.Id,
                Time = w.Time,
                Icon = "💧",
                Tint = Tint("BlueTint"),
                Title = Loc.GetString("Journal_Water"),
                Sub = Loc.Format("Journal_DrankWord", word)
            });
        }

        // Seizures (violet) — logged as they happen; optional duration + note.
        foreach (var s in await _seizures.GetForDateAsync(pet.Id, _date))
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Seizure,
                CanDelete = true,
                EntryId = s.Id,
                Time = s.Time,
                Icon = "⚡",
                Tint = Tint("VioletTint"),
                Title = Loc.GetString("Journal_Seizure"),
                Sub = SeizureSub(s)
            });
        }

        // Medication doses — placed at the moment they were tapped as taken/skipped
        // (their resolved time), falling back to the scheduled time when not yet acted on.
        items.AddRange(await GatherDoseItemsAsync(pet.Id, _date));

        // ── the single ordering: everything, purely by time ──
        var ordered = items.OrderBy(i => i.Time ?? TimeSpan.Zero).ToList();

        // Handmade wobble: alternate the icon tilt down the finished timeline.
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].IconRotation = i % 2 == 0 ? -3 : 2.5;

        return ordered;
    }

    // ── Delete a logged entry (the ✕ on a timeline card) ─────────────────────────
    // Dispatches to the row's own store. Glucose/appetite/seizure are per-row deletes;
    // mood/weight clear their columns on the day's shared PetEntry (leaving the other
    // reading intact). Every path carries an undo that restores exactly what was
    // removed. Doses are scheduled occurrences, not logs, and aren't deletable here.
    private async Task DeleteItemAsync(TimelineItem? item)
    {
        var pet = _activePet.ActivePet;
        if (item == null || !item.CanDelete || pet == null || pet.Id == 0)
            return;

        var petId = pet.Id;
        Func<Task>? undo = null;
        var message = Loc.GetString("Journal_ToastDeleted");

        switch (item.Kind)
        {
            case TimelineKind.Glucose:
            {
                var row = (await _glucose.GetForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _glucose.DeleteAsync(row.Id);
                undo = () => _glucose.InsertAsync(new GlucoseEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time, Value = row.Value, Context = row.Context
                });
                break;
            }
            case TimelineKind.Appetite:
            {
                var row = (await _appetite.GetForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _appetite.DeleteAsync(row.Id);
                undo = () => _appetite.InsertAsync(new AppetiteEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time, Level = row.Level, Food = row.Food
                });
                break;
            }
            case TimelineKind.AppetiteAmount:
            {
                var row = (await _appetite.GetAmountsForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _appetite.DeleteAmountAsync(row.Id);
                undo = () => _appetite.InsertAmountAsync(new AppetiteAmountEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time, Grams = row.Grams, Food = row.Food
                });
                break;
            }
            case TimelineKind.Seizure:
            {
                var row = (await _seizures.GetForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _seizures.DeleteAsync(row.Id);
                undo = () => _seizures.InsertAsync(new SeizureEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time,
                    DurationMinutes = row.DurationMinutes, Note = row.Note
                });
                break;
            }
            case TimelineKind.WaterAmount:
            {
                var row = (await _water.GetAmountsForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _water.DeleteAmountAsync(row.Id);
                undo = () => _water.InsertAmountAsync(new WaterAmountEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time, AmountMl = row.AmountMl
                });
                break;
            }
            case TimelineKind.WaterLevel:
            {
                var row = (await _water.GetLevelsForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _water.DeleteLevelAsync(row.Id);
                undo = () => _water.InsertLevelAsync(new WaterLevelEntry
                {
                    PetId = row.PetId, Date = row.Date, Time = row.Time, Level = row.Level
                });
                break;
            }
            case TimelineKind.Mood:
            {
                var e = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
                if (e == null || e.MoodLevel == 0) return;
                int lvl = e.MoodLevel; string mood = e.Mood; string note = e.MoodNote; long? ticks = e.MoodTimeTicks;
                e.MoodLevel = 0; e.Mood = string.Empty; e.MoodNote = string.Empty; e.MoodTimeTicks = null;
                await _petEntries.UpdatePetEntryAsync(e);
                undo = async () =>
                {
                    var cur = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
                    if (cur == null) return;
                    cur.MoodLevel = lvl; cur.Mood = mood; cur.MoodNote = note; cur.MoodTimeTicks = ticks;
                    await _petEntries.UpdatePetEntryAsync(cur);
                };
                break;
            }
            case TimelineKind.Weight:
            {
                var e = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
                if (e == null || e.Weight <= 0) return;
                decimal w = e.Weight; long? ticks = e.WeightTimeTicks;
                e.Weight = 0; e.WeightTimeTicks = null;
                await _petEntries.UpdatePetEntryAsync(e);
                undo = async () =>
                {
                    var cur = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
                    if (cur == null) return;
                    cur.Weight = w; cur.WeightTimeTicks = ticks;
                    await _petEntries.UpdatePetEntryAsync(cur);
                };
                break;
            }
            case TimelineKind.Dose:
            {
                // The ✕ on a dose clears its recorded outcome (back to "still open") —
                // it doesn't delete a row so much as undo the log. Clearing must re-arm
                // the reminder that logging cancelled (SyncMedicationAsync is idempotent);
                // undo re-applies the same outcome and re-cancels the occurrence.
                if (item.DoseOutcome is not DoseStatus prev)
                    return; // nothing recorded to clear
                var medId = item.MedicationId; var time = item.DoseTime; var date = _date;
                await _doseLogs.ClearStatusAsync(medId, date, time);
                await _reminders.SyncMedicationAsync(medId);
                undo = async () =>
                {
                    await _doseLogs.SetStatusAsync(medId, petId, date, time, prev);
                    await _reminders.MarkDoseHandledAsync(medId, date, time);
                };
                message = Loc.GetString("Journal_ToastDoseCleared");
                break;
            }
            default:
                return; // any other non-deletable kind
        }

        if (undo == null)
            return;

        ItemDeleted?.Invoke(new JournalSaveResult(message, undo));
    }

    // ── Mark a dose as skipped (the "Mark as skipped" button on a dose card) ──────
    // A skip is a first-class outcome, not a delete: it records deliberate
    // non-adherence (which the vet report counts) and stops the reminder nagging.
    // Undo restores whatever the dose was before — usually "still open", occasionally
    // an earlier "given". Reuses the same refresh + undo-toast path as a delete.
    private async Task SkipDoseAsync(TimelineItem? item)
    {
        var pet = _activePet.ActivePet;
        if (item == null || !item.IsDose || !item.DoseActionable || pet == null || pet.Id == 0)
            return;
        if (item.DoseOutcome == DoseStatus.Skipped)
            return; // already skipped — nothing to do

        var petId = pet.Id;
        var medId = item.MedicationId;
        var time = item.DoseTime;
        var date = _date;
        var prev = item.DoseOutcome;

        await _doseLogs.SetStatusAsync(medId, petId, date, time, DoseStatus.Skipped);
        // A skipped dose is handled — don't let its reminder fire late or re-send.
        await _reminders.MarkDoseHandledAsync(medId, date, time);

        Func<Task> undo = async () =>
        {
            if (prev is DoseStatus previous)
            {
                await _doseLogs.SetStatusAsync(medId, petId, date, time, previous);
                await _reminders.MarkDoseHandledAsync(medId, date, time);
            }
            else
            {
                // Was still open → clear back to pending and re-arm its reminder.
                await _doseLogs.ClearStatusAsync(medId, date, time);
                await _reminders.SyncMedicationAsync(medId);
            }
        };

        ItemDeleted?.Invoke(new JournalSaveResult(
            Loc.Format("Journal_ToastMedSkipped", item.Title), undo));
    }

    // Today's scheduled doses as timeline entries, from the shared DayDoseService
    // (same meds → schedules → logs join the pending engine + Calendar use). A dose
    // sits at its resolved (tapped) time when acted on, else at its scheduled time.
    private async Task<List<TimelineItem>> GatherDoseItemsAsync(int petId, DateTime date)
    {
        var now = DateTime.Now;
        var honey = Tint("HoneyWarmTint");
        var result = new List<TimelineItem>();

        foreach (var d in await _dayDoses.GetForDayAsync(petId, date))
        {
            var canToggle = date < now.Date || (date == now.Date && d.ScheduledTime <= now.TimeOfDay);
            var outcome = d.Log?.Status;
            result.Add(new TimelineItem
            {
                Kind = TimelineKind.Dose,
                Time = d.Log?.ResolvedAt?.TimeOfDay ?? d.ScheduledTime,
                Icon = "💊",
                Tint = honey,
                Title = d.Medication.Name,
                Sub = $"{d.Medication.Dosage} {d.Medication.Unit} · {DoseStatusText(d.Log, canToggle)}",
                MedicationId = d.Medication.Id,
                DoseTime = d.ScheduledTime,
                DoseOutcome = outcome,
                DoseActionable = canToggle,
                // The ✕ clears a recorded outcome, so only offer it once one exists
                // (a dose still "open" has nothing to undo — it's a chip, not a delete).
                CanDelete = canToggle && outcome is DoseStatus.Taken or DoseStatus.Skipped
            });
        }

        return result;
    }

    private static string DoseStatusText(MedicationDoseLog? log, bool canToggle) => log?.Status switch
    {
        DoseStatus.Taken => Loc.GetString("Dose_Taken"),
        DoseStatus.Skipped => Loc.GetString("Dose_Skipped"),
        DoseStatus.Missed => Loc.GetString("Dose_Missed"),
        _ => Loc.GetString(canToggle ? "Dose_NotTaken" : "Dose_Upcoming")
    };

    // Append an optional food label to an appetite line: "Ate everything · chicken".
    private static string WithFood(string basis, string? food)
    {
        food = food?.Trim() ?? string.Empty;
        return food.Length > 0 ? $"{basis} · {food}" : basis;
    }

    // Duration and note are both optional; join whichever are present.
    private static string SeizureSub(SeizureEntry s)
    {
        var duration = s.DurationMinutes is int m ? Loc.Format("Journal_SeizureDuration", m) : string.Empty;
        var note = s.Note?.Trim() ?? string.Empty;
        if (duration.Length > 0 && note.Length > 0)
            return $"{duration} · {note}";
        return duration.Length > 0 ? duration : note;
    }

    /// <summary>Ticks → time of day, or null when the entry has no stored time.</summary>
    private static TimeSpan? TicksToTime(long? ticks) =>
        ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : null;

    /// <summary>Resolve a rockpool colour token to a <see cref="Color"/> for an icon tile.</summary>
    private static Color Tint(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c
            : Colors.Transparent;

    // "{Before|After} food" — plus a gentle range sentence ONLY when a range exists.
    // The value itself is never coloured or altered by the range.
    private static string GlucoseSub(GlucoseEntry g, TargetRange? range)
    {
        var context = Loc.GetString(g.Context == FoodContext.BeforeFood ? "Journal_BeforeFood" : "Journal_AfterFood");
        if (range is not { } r)
            return context;

        string sentence = Loc.GetString(
            r.Contains(g.Value) ? "Journal_GlucoseInRange"
            : g.Value > r.Hi ? "Journal_GlucoseHigh"
            : "Journal_GlucoseLow");
        return $"{context} · {sentence}";
    }

    // ── One-tap dose logging (with undo) ──────────────────────────────────────────
    public async Task<JournalSaveResult> LogDoseAsync(JournalChip chip)
    {
        await _doseLogs.SetStatusAsync(chip.MedicationId, chip.PetId, _date, chip.DoseTime, DoseStatus.Taken);
        // Don't let this occurrence's reminder fire late or re-send.
        await _reminders.MarkDoseHandledAsync(chip.MedicationId, _date, chip.DoseTime);

        var medId = chip.MedicationId;
        var time = chip.DoseTime;
        var date = _date;
        return new JournalSaveResult(
            Loc.Format("Journal_ToastMedGiven", chip.Label),
            async () =>
            {
                await _doseLogs.ClearStatusAsync(medId, date, time);
                // MarkDoseHandledAsync cancelled this occurrence's reminder; undoing
                // the log must re-arm it, or an accidental tap + undo before the due
                // time would silently kill the reminder (and its boot re-send).
                // SyncMedicationAsync is idempotent and rebuilds pending occurrences.
                await _reminders.SyncMedicationAsync(medId);
            });
    }

    // ── Add-anything options (tracker sheets present in the plan) ──────────────────
    private async Task BuildAddOptionsAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null)
        {
            AddOptions = System.Array.Empty<AddOption>();
            return;
        }

        var plan = await _carePlan.GetPlanAsync(pet);
        bool Has(TrackerId id) => plan.Any(t => t.TrackerId == id);

        // Build a fresh list and assign it (see AddOptions' note above).
        var options = new List<AddOption>();
        if (Has(TrackerId.Glucose)) options.Add(new AddOption { Kind = JournalChipKind.Glucose, Icon = "🩸", Label = Loc.GetString("Journal_GlucoseCheck") });
        options.Add(new AddOption { Kind = JournalChipKind.Mood, Icon = "🙂", Label = Loc.GetString("Journal_MoodTitle") });
        if (Has(TrackerId.Appetite)) options.Add(new AddOption { Kind = JournalChipKind.Appetite, Icon = "🍽️", Label = Loc.GetString("Journal_Appetite") });
        if (Has(TrackerId.Water)) options.Add(new AddOption { Kind = JournalChipKind.Water, Icon = "💧", Label = Loc.GetString("Journal_Water") });
        options.Add(new AddOption { Kind = JournalChipKind.Weight, Icon = "⚖️", Label = Loc.GetString("Journal_WeighIn") });
        if (Has(TrackerId.Seizure)) options.Add(new AddOption { Kind = JournalChipKind.Seizure, Icon = "⚡", Label = Loc.GetString("Journal_Seizure") });
        AddOptions = options;
    }

    private void NotifyStates()
    {
        OnPropertyChanged(nameof(ShowAllDone));
        OnPropertyChanged(nameof(ShowStillToDo));
        OnPropertyChanged(nameof(ShowPastAdd));
    }

    private void RaiseTimelineFlags()
    {
        OnPropertyChanged(nameof(HasTimelineItems));
        OnPropertyChanged(nameof(IsTimelineEmpty));
    }
}
