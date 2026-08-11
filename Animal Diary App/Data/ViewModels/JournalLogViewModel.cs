namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
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

public enum JournalChipKind { Medication, Glucose, Mood, Appetite, Weight, Seizure, Water, Custom, Add }

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

    /// <summary>Which tracker this chip logs. Load-bearing for CUSTOM chips only: every
    /// one of them carries <see cref="JournalChipKind.Custom"/>, so the kind alone can't
    /// say which of the owner's trackers was tapped.</summary>
    public TrackerKey Tracker { get; init; }
}

/// <summary>Which kind of entry a <see cref="TimelineItem"/> represents. Drives only
/// the template pick (Mood gets the washi-note card, everything else the standard
/// card) — never the ordering, which is purely by time.</summary>
public enum TimelineKind { Mood, Weight, Glucose, Appetite, AppetiteAmount, Seizure, WaterAmount, WaterLevel, Custom, Dose }

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

    /// <summary>Show the "Mark as given" button: an actionable dose not already taken.
    /// This is the path back from a Missed (or Skipped) dose — a pill given late or
    /// logged after the fact — and stays available on a still-open past dose.</summary>
    public bool CanGiveDose => IsDose && DoseActionable && DoseOutcome != DoseStatus.Taken;

    /// <summary>Show the "Mark as skipped" button: an actionable dose not already skipped.</summary>
    public bool CanSkipDose => IsDose && DoseActionable && DoseOutcome != DoseStatus.Skipped;
}

/// <summary>A log type offered in the "+" (add-anything) sheet.</summary>
public class AddOption
{
    public JournalChipKind Kind { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;

    /// <summary>Which tracker this option logs — see <see cref="JournalChip.Tracker"/>.</summary>
    public TrackerKey Tracker { get; init; }
}

public class JournalLogViewModel : BaseViewModel
{
    private readonly PendingItemsService _pending;
    private readonly CarePlanService _carePlan;
    private readonly ActivePetService _activePet;
    private readonly DayDoseService _dayDoses;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly MedicationReminderScheduler _reminders;
    private readonly DailyCareReminderScheduler _dailyReminders;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly SeizureEntryService _seizures;
    private readonly WaterEntryService _water;
    private readonly CustomTrackerService _custom;
    private readonly IAnalyticsService _analytics;

    private DateTime _date = DateTime.Now.Date;

    /// <summary>The pet's own tracker definitions by row id, published by the atomic fill
    /// of whichever reload finished last — read only by the chip builders, which run inside
    /// that same fill.
    ///
    /// <para>ARCHIVED ones are included, deliberately: an entry outlives the retirement of
    /// the tracker that collected it, and a timeline card still needs its name, emoji and
    /// colour to render. Reading only the live ones would blank out history the moment
    /// someone tidied their care plan.</para></summary>
    private Dictionary<int, CustomTracker> _customById = new();

    public JournalLogViewModel(
        PendingItemsService pending,
        CarePlanService carePlan,
        ActivePetService activePet,
        DayDoseService dayDoses,
        MedicationDoseLogService doseLogs,
        MedicationReminderScheduler reminders,
        DailyCareReminderScheduler dailyReminders,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        SeizureEntryService seizures,
        WaterEntryService water,
        CustomTrackerService custom,
        IAnalyticsService analytics)
    {
        _pending = pending;
        _carePlan = carePlan;
        _activePet = activePet;
        _dayDoses = dayDoses;
        _doseLogs = doseLogs;
        _reminders = reminders;
        _dailyReminders = dailyReminders;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _seizures = seizures;
        _water = water;
        _custom = custom;
        _analytics = analytics;

        OpenAddSheetCommand = new Command(async () => await OpenAddSheetAsync());
        CloseAddSheetCommand = new Command(() => IsAddSheetVisible = false);
        SelectAddOptionCommand = new Command<AddOption>(OnSelectAddOption);
        DeleteItemCommand = new Command<TimelineItem>(async i => await DeleteItemAsync(i));
        GiveDoseCommand = new Command<TimelineItem>(async i => await GiveDoseAsync(i));
        SkipDoseCommand = new Command<TimelineItem>(async i => await SkipDoseAsync(i));
    }

    // Short-hand for the localized string manager (usable from the static chip builders).
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Raised when the person picks a log type (from a chip or the "+"
    /// sheet). The page opens the matching sheet — it owns the sheet VMs + animations.
    ///
    /// <para>The <see cref="TrackerKey"/> rides along because every owner-defined tracker
    /// shares one <see cref="JournalChipKind.Custom"/>; for the shipped kinds it is simply
    /// unused.</para></summary>
    public event Action<JournalChipKind, TrackerKey>? RequestOpenSheet;

    /// <summary>Raised after a timeline entry is deleted, carrying the confirmation
    /// line + an undo that restores it — the page shows the standard undo-toast and
    /// refreshes (same safety net every destructive Journal action uses).</summary>
    public event Action<JournalSaveResult>? ItemDeleted;

    /// <summary>Delete one logged timeline entry (the ✕ on a card). Dispatches to the
    /// right store by kind; a dose's ✕ clears its recorded outcome instead.</summary>
    public ICommand DeleteItemCommand { get; }

    /// <summary>Mark a dose as given (the "Mark as given" button on a dose card).
    /// The way to record a pill given late or logged after the fact — including a
    /// dose the reconciler already marked Missed.</summary>
    public ICommand GiveDoseCommand { get; }

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

    /// <summary>
    /// The log types NOT in this pet's care plan, offered below the plan's own in the
    /// "+" sheet. Logging never required a tracker — no sheet ViewModel reads the care
    /// plan — so the plan was only ever deciding what the Journal ASKS for, while the
    /// sheet quietly made everything else unreachable. A diabetes owner who wanted to
    /// note water had to claim their pet also had kidney disease.
    ///
    /// Picking one here logs it, once. It does NOT join the care plan: that is a
    /// separate, deliberate act in Manage, so a single entry can't sign someone up to
    /// be asked about it every day.
    /// </summary>
    private IReadOnlyList<AddOption> _moreOptions = System.Array.Empty<AddOption>();
    public IReadOnlyList<AddOption> MoreOptions
    {
        get => _moreOptions;
        private set
        {
            if (SetProperty(ref _moreOptions, value))
                OnPropertyChanged(nameof(HasMoreOptions));
        }
    }

    /// <summary>Whether the second group has anything in it — hides its heading and
    /// hint once a pet's plan already covers everything.</summary>
    public bool HasMoreOptions => MoreOptions.Count > 0;

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
        RequestOpenSheet?.Invoke(option.Kind, option.Tracker);
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

        // The owner's own tracker definitions, as a LOCAL snapshot that travels with this
        // run. Two reloads are routinely in flight (see below), and a shared field written
        // before the awaits would let one run's timeline render against another's
        // definitions — every lookup a miss, so every custom card blank. The field is
        // assigned in the atomic fill instead, where the chips read it in the same breath.
        var customById = _hasPet
            ? (await _custom.GetAllForPetAsync(pet!.Id)).ToDictionary(c => c.Id)
            : new Dictionary<int, CustomTracker>();

        // Gather everything (the awaits) BEFORE touching the observable collections.
        // Several reloads fire on startup (OnAppearing + the date/pet PropertyChanged
        // handlers); if we cleared before awaiting, their clear+add would interleave
        // and duplicate the rows. The fill below is await-free, so each reload rebuilds
        // atomically on the UI thread.
        var timeline = await GatherTimelineAsync(pet, customById);

        IReadOnlyList<PendingItem> pending = System.Array.Empty<PendingItem>();
        if (_hasPet && IsToday)
            pending = await _pending.GetAsync(pet!, _date);

        // ── atomic fill: no awaits from here on ──
        _customById = customById;
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

        // After-write hook: a logging change today alters what's still pending, so
        // re-evaluate the daily care reminder (it may now need cancelling because the
        // day is handled, or arming). Fire-and-forget — it has its own gate and must
        // not slow the Journal reload. Only relevant for today's board.
        if (IsToday)
            _dailyReminders.RefreshAsync().Forget();
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

    // Icon + label come from the shared TrackerVisuals table; only the chip kind and
    // the screen-reader phrasing are Journal-specific. Glucose is the one chip with a
    // trailing "{done} of {target}" detail, so it doesn't go through Simple().
    private JournalChip TrackerChip(PendingItem item, double tilt)
    {
        // An owner-defined tracker: its name, emoji and colour are its own row's, and the
        // chip's Kind can't identify it, so the key rides along for the page to route on.
        if (item.Tracker is { IsCustom: true } key)
        {
            var def = _customById.GetValueOrDefault(key.CustomId);
            var visual = CustomTrackerVisuals.For(def);
            var label = def?.Name ?? string.Empty;
            return new JournalChip
            {
                Kind = JournalChipKind.Custom,
                Icon = visual.Icon,
                Label = label,
                Tilt = tilt,
                Tracker = key,
                SemanticLabel = Loc.Format("Journal_A11yLogCustom", label),
            };
        }

        var v = TrackerVisuals.For(item.Tracker);

        if (item.Tracker?.Is(TrackerId.Glucose) == true)
        {
            return new JournalChip
            {
                Kind = JournalChipKind.Glucose,
                Icon = v.Icon,
                Label = Loc.GetString(v.LabelKey),
                Detail = Loc.Format("Journal_CountOfN", item.Done, item.Target),
                Tilt = tilt,
                SemanticLabel = Loc.Format("Journal_A11yLogGlucose", item.Done, item.Target)
            };
        }

        var (kind, a11yKey) = item.Tracker?.BuiltIn switch
        {
            TrackerId.Appetite => (JournalChipKind.Appetite, "Journal_A11yLogAppetite"),
            TrackerId.Weight => (JournalChipKind.Weight, "Journal_A11yLogWeight"),
            TrackerId.Water => (JournalChipKind.Water, "Journal_A11yLogWater"),
            _ => (JournalChipKind.Mood, "Journal_A11yLogMood"),
        };

        return Simple(kind, v.Icon, Loc.GetString(v.LabelKey), tilt, Loc.GetString(a11yKey));
    }

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
    private async Task<List<TimelineItem>> GatherTimelineAsync(
        Pet? pet, IReadOnlyDictionary<int, CustomTracker> customById)
    {
        var items = new List<TimelineItem>();

        if (pet == null || pet.Id == 0)
            return items;

        // Every store for the day, issued together rather than one awaited round trip
        // after another. None of them depends on another's result, and this method runs
        // on every Journal appearance, every date change, every save and every
        // RemoteChangesApplied — nine sequential hops was most of the day view's latency.
        // (Doses stay separate below: that call already batches its own joins.)
        var entryTask = _petEntries.GetPetEntryByDateAndPetIdAsync(_date, pet.Id);
        var planTask = _carePlan.GetPlanAsync(pet);
        var glucoseTask = _glucose.GetForDateAsync(pet.Id, _date);
        var appetiteTask = _appetite.GetForDateAsync(pet.Id, _date);
        var appetiteAmountTask = _appetite.GetAmountsForDateAsync(pet.Id, _date);
        var waterAmountTask = _water.GetAmountsForDateAsync(pet.Id, _date);
        var waterLevelTask = _water.GetLevelsForDateAsync(pet.Id, _date);
        var seizureTask = _seizures.GetForDateAsync(pet.Id, _date);
        // ONE query covering every custom tracker the pet has, however many that is —
        // grouped below. This is what keeps "as many as you like" free here.
        var customTask = _custom.GetForDateAsync(pet.Id, _date);

        await Task.WhenAll(entryTask, planTask, glucoseTask, appetiteTask,
            appetiteAmountTask, waterAmountTask, waterLevelTask, seizureTask, customTask);

        // Mood + Weight (both live on the day's PetEntry, each with its own time).
        var entry = entryTask.Result;
        if (entry != null)
        {
            if (entry.MoodLevel > 0)
            {
                // A recorded mood, so show the one the owner actually chose rather than
                // a fixed face. (The Journal CHIP keeps its neutral icon: that one stands
                // for "mood check still to do", where there is no mood to show yet.)
                var mood = (MoodLevel)entry.MoodLevel;
                var moodWord = mood.GetDisplayName();
                items.Add(new TimelineItem
                {
                    Kind = TimelineKind.Mood,
                    CanDelete = true,
                    Time = TicksToTime(entry.MoodTimeTicks),
                    Icon = mood.GetEmoji(),
                    Tint = TrackerTint(TrackerId.Mood),
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
                    Icon = TrackerVisuals.For(TrackerId.Weight).Icon,
                    Tint = TrackerTint(TrackerId.Weight),
                    Title = Loc.GetString("Journal_WeighIn"),
                    Sub = $"{weight} · {Loc.GetString("Journal_WeightScales")}"
                });
            }
        }

        // Glucose (rose) — value is precise; range sentence only when a range exists.
        var range = planTask.Result
            .FirstOrDefault(t => t.Key.Is(TrackerId.Glucose))?.TargetRange;
        foreach (var g in glucoseTask.Result)
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Glucose,
                CanDelete = true,
                EntryId = g.Id,
                Time = g.Time,
                Icon = TrackerVisuals.For(TrackerId.Glucose).Icon,
                Tint = TrackerTint(TrackerId.Glucose),
                Title = Loc.Format("Journal_GlucoseTimeline", g.Value.ToString("0.0", CultureInfo.CurrentCulture)),
                Sub = GlucoseSub(g, range)
            });
        }

        // Appetite (honey) — two kinds that can both appear: the day's qualitative
        // reading (Didn't eat … everything) and any exact grams events. Both may carry
        // a food label. Never judged.
        foreach (var a in appetiteTask.Result)
        {
            var word = ((AppetiteLevel)a.Level).GetDisplayName().ToLowerInvariant();
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Appetite,
                CanDelete = true,
                EntryId = a.Id,
                Time = a.Time,
                Icon = TrackerVisuals.For(TrackerId.Appetite).Icon,
                Tint = TrackerTint(TrackerId.Appetite),
                Title = Loc.GetString("Journal_Appetite"),
                Sub = WithFood(Loc.Format("Journal_AteWord", word), a.Food)
            });
        }
        foreach (var a in appetiteAmountTask.Result)
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.AppetiteAmount,
                CanDelete = true,
                EntryId = a.Id,
                Time = a.Time,
                Icon = TrackerVisuals.For(TrackerId.Appetite).Icon,
                Tint = TrackerTint(TrackerId.Appetite),
                Title = Loc.GetString("Journal_Appetite"),
                Sub = WithFood(Loc.Format("Journal_AppetiteGrams", a.Grams.ToString("0.#", CultureInfo.CurrentCulture)), a.Food)
            });
        }

        // Water (blue) — two independent kinds that can both appear on a day:
        //   • exact ml readings, one card each (additive events), and
        //   • the day's single relative reading (Barely … a lot).
        // Never judged; the value is a plain fact.
        foreach (var w in waterAmountTask.Result)
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.WaterAmount,
                CanDelete = true,
                EntryId = w.Id,
                Time = w.Time,
                Icon = TrackerVisuals.For(TrackerId.Water).Icon,
                Tint = TrackerTint(TrackerId.Water),
                Title = Loc.GetString("Journal_Water"),
                Sub = Loc.Format("Journal_WaterMl", w.AmountMl.ToString("0.#", CultureInfo.CurrentCulture))
            });
        }
        foreach (var w in waterLevelTask.Result)
        {
            var word = ((WaterLevel)w.Level).GetDisplayName().ToLowerInvariant();
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.WaterLevel,
                CanDelete = true,
                EntryId = w.Id,
                Time = w.Time,
                Icon = TrackerVisuals.For(TrackerId.Water).Icon,
                Tint = TrackerTint(TrackerId.Water),
                Title = Loc.GetString("Journal_Water"),
                Sub = Loc.Format("Journal_DrankWord", word)
            });
        }

        // Seizures (violet) — logged as they happen; optional duration + note.
        foreach (var s in seizureTask.Result)
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Seizure,
                CanDelete = true,
                EntryId = s.Id,
                Time = s.Time,
                Icon = TrackerVisuals.For(TrackerId.Seizure).Icon,
                Tint = TrackerTint(TrackerId.Seizure),
                Title = Loc.GetString("Journal_Seizure"),
                Sub = SeizureSub(s)
            });
        }

        // The owner's own trackers. One card per entry (they are events), wearing the
        // name, emoji and colour from the definition — including a RETIRED one, so
        // tidying the care plan never erases what was already written down.
        foreach (var c in customTask.Result)
        {
            var def = customById.GetValueOrDefault(c.CustomTrackerId);
            var visual = CustomTrackerVisuals.For(def);
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Custom,
                CanDelete = true,
                EntryId = c.Id,
                Time = c.Time,
                Icon = visual.Icon,
                Tint = Tint(visual.TintKey),
                Title = def?.Name ?? string.Empty,
                Sub = CustomEntrySheetViewModel.Describe(c, def),
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
            case TimelineKind.Custom:
            {
                var row = (await _custom.GetForDateAsync(petId, _date)).FirstOrDefault(r => r.Id == item.EntryId);
                if (row == null) return;
                await _custom.DeleteEntryAsync(row.Id);
                undo = () => _custom.InsertAsync(new CustomEntry
                {
                    PetId = row.PetId, CustomTrackerId = row.CustomTrackerId, Date = row.Date,
                    Time = row.Time, Amount = row.Amount, Note = row.Note
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

    /// <summary>Emit <c>dose_logged</c> for a dose outcome the owner just recorded. Only
    /// the coarse taken/skipped bucket is sent — never the medication, dose, time, or pet.
    ///
    /// <para>Called from the three user gestures (chip tap, "Mark as given", "Mark as
    /// skipped") rather than from <see cref="MedicationDoseLogService"/>, which would also
    /// catch the reconciler stamping doses Missed on its own — a machine action, not a
    /// person caring for a pet. Forward paths only: undo doesn't fire it again, matching
    /// <c>journal_entry_created</c>.</para></summary>
    private void TrackDoseLogged(string status)
    {
        _analytics.Track(AnalyticsEvents.DoseLogged, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropDoseStatus] = status,
        });
    }

    // ── Mark a dose as given (the "Mark as given" button on a dose card) ──────────
    // The timeline counterpart to one-tap chip logging, for a dose that's no longer a
    // chip: a pill given late, forgotten and logged after the fact, or one the
    // reconciler already stamped Missed. Records Taken, cancels the (late) reminder,
    // and offers undo restoring whatever the dose was before (Missed / Skipped / open).
    private async Task GiveDoseAsync(TimelineItem? item)
    {
        var pet = _activePet.ActivePet;
        if (item == null || !item.IsDose || !item.DoseActionable || pet == null || pet.Id == 0)
            return;
        if (item.DoseOutcome == DoseStatus.Taken)
            return; // already given — nothing to do

        var petId = pet.Id;
        var medId = item.MedicationId;
        var time = item.DoseTime;
        var date = _date;
        var prev = item.DoseOutcome;

        await _doseLogs.SetStatusAsync(medId, petId, date, time, DoseStatus.Taken);
        // A given dose is handled — don't let its reminder fire late or re-send.
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

        TrackDoseLogged(AnalyticsEvents.DoseStatusTaken);

        ItemDeleted?.Invoke(new JournalSaveResult(
            Loc.Format("Journal_ToastMedGiven", item.Title), undo));
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

        TrackDoseLogged(AnalyticsEvents.DoseStatusSkipped);

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
    // Type · duration · note, in that order, with whichever parts were answered. All three
    // are optional, so a seizure logged with only a time has an empty sub line.
    private static string SeizureSub(SeizureEntry s)
    {
        var parts = new List<string>(3);
        if (s.Type is SeizureType t)
            parts.Add(t.GetDisplayName());
        if (s.DurationMinutes is int m)
            parts.Add(Loc.Format("Journal_SeizureDuration", m));
        var note = s.Note?.Trim() ?? string.Empty;
        if (note.Length > 0)
            parts.Add(note);
        return string.Join(" · ", parts);
    }

    /// <summary>Ticks → time of day, or null when the entry has no stored time.</summary>
    private static TimeSpan? TicksToTime(long? ticks) =>
        ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : null;

    /// <summary>Resolve a rockpool colour token to a <see cref="Color"/> for an icon tile.</summary>
    private static Color Tint(string key) => AppColors.Resolve(key);

    /// <summary>The icon-tile tint a tracker's timeline cards wear.</summary>
    private static Color TrackerTint(TrackerId id) => Tint(TrackerVisuals.For(id).TintKey);

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

        TrackDoseLogged(AnalyticsEvents.DoseStatusTaken);

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

    // ── Add-anything options ──────────────────────────────────────────────────────
    // Everything the app can record, in two groups: the pet's plan first, then the
    // rest. The rest used to be hidden entirely, which made a built-in log type look
    // unavailable when it was only unasked-for. The "still to do" CHIP row is
    // deliberately not changed — that one is a to-do list, and putting unopted-in
    // trackers there would nag about things nobody agreed to.
    // Only the ordering and the tracker→chip-kind pairing live here; the icon and
    // label come from TrackerVisuals, so a new tracker is one line there plus one here.
    private static readonly (TrackerId Tracker, JournalChipKind Kind)[] LoggableTypes =
    {
        (TrackerId.Glucose,  JournalChipKind.Glucose),
        (TrackerId.Mood,     JournalChipKind.Mood),
        (TrackerId.Appetite, JournalChipKind.Appetite),
        (TrackerId.Water,    JournalChipKind.Water),
        (TrackerId.Weight,   JournalChipKind.Weight),
        (TrackerId.Seizure,  JournalChipKind.Seizure),
    };

    private async Task BuildAddOptionsAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null)
        {
            AddOptions = System.Array.Empty<AddOption>();
            MoreOptions = System.Array.Empty<AddOption>();
            return;
        }

        var plan = await _carePlan.GetPlanAsync(pet);
        bool Has(TrackerId id) => plan.Any(t => t.Key.Is(id));

        // Mood and weight are in every pet's default plan, so they normally land in the
        // first group on their own merit; Has() decides for them like everything else.
        var inPlan = new List<AddOption>();
        var rest = new List<AddOption>();
        foreach (var (tracker, kind) in LoggableTypes)
        {
            var v = TrackerVisuals.For(tracker);
            var option = new AddOption { Kind = kind, Icon = v.Icon, Label = Loc.GetString(v.LabelKey) };
            (Has(tracker) ? inPlan : rest).Add(option);
        }

        // The owner's own trackers, always in the FIRST group: unlike a shipped type,
        // one exists only because they deliberately made it, so it is never "something
        // else the app can also record". Retired ones are absent — they were retired to
        // stop being offered — which is why this reads the live list, not _customById.
        foreach (var c in await _custom.GetForPetAsync(pet.Id))
        {
            var visual = CustomTrackerVisuals.For(c);
            inPlan.Add(new AddOption
            {
                Kind = JournalChipKind.Custom,
                Icon = visual.Icon,
                Label = c.Name,
                Tracker = c.Key,
            });
        }

        // Build fresh lists and assign them (see AddOptions' note above).
        AddOptions = inPlan;
        MoreOptions = rest;
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
