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
//  All NEW functionality: the pending list, one-tap dose logging with undo, care-
//  plan access, so it lives here rather than reshaping the CalendarViewModel. The
//  page coordinates the sheets + animations (bubble-pop, toast); this VM owns the
//  data and the medical actions. The word "Diabetes" never appears here: it only
//  knows trackers and med doses.
// ─────────────────────────────────────────────────────────────────────────────

// VetQuestion is a routing key only. It names a row in the "+" sheet and the sheet
// that row opens: it is never built into a chip, because a question is not a thing to
// be done today. BuildChips reads PendingItems, which come from the care plan, and a
// question is not a tracker, so it cannot appear there by construction.
public enum JournalChipKind { Medication, Glucose, Mood, Appetite, Weight, Seizure, Water, Custom, Add, VetQuestion }

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

    /// <summary>Slight alternating tilt (±0.4°): imperfection on the frame only.</summary>
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
/// card), never the ordering, which is purely by time.</summary>
public enum TimelineKind { Mood, Weight, Glucose, Appetite, AppetiteAmount, Seizure, WaterAmount, WaterLevel, Custom, Dose }

/// <summary>One entry on the Journal's single chronological timeline, whatever its
/// kind. Everything logged for the day: mood, weight, glucose, appetite, seizures
/// and medication doses: becomes one of these and is sorted purely by
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
    /// reading; for a dose it means "an outcome is recorded": the ✕ then clears that
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

    /// <summary>Slight alternating tilt down the timeline: imperfection on the frame.
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
    /// <summary>Medication this dose belongs to: for the ✕ (clear) and skip actions.</summary>
    public int MedicationId { get; init; }

    /// <summary>The dose's scheduled time-of-day (its key, not its resolved time).</summary>
    public TimeSpan DoseTime { get; init; }

    /// <summary>The dose's recorded outcome, or null when it hasn't been acted on.</summary>
    public DoseStatus? DoseOutcome { get; init; }

    /// <summary>A past or already-due dose (never a future occurrence): the only ones
    /// that can be skipped or cleared.</summary>
    public bool DoseActionable { get; init; }

    public bool IsDose => Kind == TimelineKind.Dose;

    /// <summary>Show the "Mark as given" button: an actionable dose not already taken.
    /// This is the path back from a Missed (or Skipped) dose: a pill given late or
    /// logged after the fact, and stays available on a still-open past dose.</summary>
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

    /// <summary>Which tracker this option logs: see <see cref="JournalChip.Tracker"/>.</summary>
    public TrackerKey Tracker { get; init; }
}

public class JournalLogViewModel : BaseViewModel
{
    private readonly PendingItemsService _pending;
    private readonly CarePlanService _carePlan;
    private readonly ActivePetService _activePet;
    private readonly DayDoseService _dayDoses;
    private readonly MedicationDoseLogService _doseLogs;
    private readonly MedicationService _medications;
    private readonly MedicationReminderScheduler _reminders;
    private readonly DailyCareReminderScheduler _dailyReminders;
    private readonly PetEntryService _petEntries;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;
    private readonly SeizureEntryService _seizures;
    private readonly WaterEntryService _water;
    private readonly CustomTrackerService _custom;
    private readonly DisplayUnitService _displayUnits;
    private readonly IAnalyticsService _analytics;

    private DateTime _date = DateTime.Now.Date;

    /// <summary>The pet's own tracker definitions by row id, published by the atomic fill
    /// of whichever reload finished last: read only by the chip builders, which run inside
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
        MedicationService medications,
        MedicationReminderScheduler reminders,
        DailyCareReminderScheduler dailyReminders,
        PetEntryService petEntries,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite,
        SeizureEntryService seizures,
        WaterEntryService water,
        CustomTrackerService custom,
        DisplayUnitService displayUnits,
        IAnalyticsService analytics)
    {
        _pending = pending;
        _carePlan = carePlan;
        _activePet = activePet;
        _dayDoses = dayDoses;
        _doseLogs = doseLogs;
        _medications = medications;
        _reminders = reminders;
        _dailyReminders = dailyReminders;
        _petEntries = petEntries;
        _glucose = glucose;
        _appetite = appetite;
        _seizures = seizures;
        _water = water;
        _custom = custom;
        _displayUnits = displayUnits;
        _analytics = analytics;

        OpenAddSheetCommand = new Command(async () => await OpenAddSheetAsync());
        CloseAddSheetCommand = new Command(() => IsAddSheetVisible = false);
        SelectAddOptionCommand = new Command<AddOption>(OnSelectAddOption);
        DeleteItemCommand = new Command<TimelineItem>(async i => await DeleteItemAsync(i));
        AskAboutItemCommand = new Command<TimelineItem>(AskAbout);
        GiveDoseCommand = new Command<TimelineItem>(async i => await GiveDoseAsync(i));
        SkipDoseCommand = new Command<TimelineItem>(async i => await SkipDoseAsync(i));
    }

    // Short-hand for the localized string manager (usable from the static chip builders).
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Raised when the person picks a log type (from a chip or the "+"
    /// sheet). The page opens the matching sheet: it owns the sheet VMs + animations.
    ///
    /// <para>The <see cref="TrackerKey"/> rides along because every owner-defined tracker
    /// shares one <see cref="JournalChipKind.Custom"/>; for the shipped kinds it is simply
    /// unused.</para></summary>
    public event Action<JournalChipKind, TrackerKey>? RequestOpenSheet;

    /// <summary>Raised after a timeline entry is deleted, carrying the confirmation
    /// line + an undo that restores it: the page shows the standard undo-toast and
    /// refreshes (same safety net every destructive Journal action uses).</summary>
    public event Action<JournalSaveResult>? ItemDeleted;

    /// <summary>Delete one logged timeline entry (the ✕ on a card). Dispatches to the
    /// right store by kind; a dose's ✕ clears its recorded outcome instead.</summary>
    public ICommand DeleteItemCommand { get; }

    /// <summary>Raise a vet question from the entry that provoked it. Swipe-only: see
    /// AskAbout.</summary>
    public ICommand AskAboutItemCommand { get; }

    /// <summary>Mark a dose as given (the "Mark as given" button on a dose card).
    /// The way to record a pill given late or logged after the fact: including a
    /// dose the reconciler already marked Missed.</summary>
    public ICommand GiveDoseCommand { get; }

    /// <summary>Mark a dose as skipped (the "Mark as skipped" button on a dose card).
    /// A skip is a first-class non-adherence fact that reaches the vet report.</summary>
    public ICommand SkipDoseCommand { get; }

    // ── Chip row ────────────────────────────────────────────────────────────────
    /// <summary>Range-batched: a chip row is rebuilt on every appearance, every save and
    /// every date change, and Clear() + per-item Add() made BindableLayout instantiate a
    /// template and invalidate layout once PER CHIP. See RangeObservableCollection.</summary>
    public RangeObservableCollection<JournalChip> Chips { get; } = new();

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

    /// <summary>Today, and everything's done: show the paw celebration.</summary>
    public bool ShowAllDone => IsToday && !HasPending && _hasPet;

    /// <summary>Today, with things left: show the heading + chips.</summary>
    public bool ShowStillToDo => IsToday && HasPending;

    /// <summary>A past day, no pending logic, just a single "add" affordance.</summary>
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
    // TimelineItems here and are sorted purely by time: a single ordering, no
    // per-kind sections. The page renders them with one template selector.
    /// <summary>Range-batched: see <see cref="Chips"/>. This is the list whose length
    /// the owner controls without bound, so it is the one that most needed it: a busy
    /// day rebuilt ~20 cards of ~22 elements each, one layout pass apiece.</summary>
    public RangeObservableCollection<TimelineItem> TimelineItems { get; } = new();

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
    /// "+" sheet. Logging never required a tracker, no sheet ViewModel reads the care
    /// plan, so the plan was only ever deciding what the Journal ASKS for, while the
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

    /// <summary>Whether the second group has anything in it: hides its heading and
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
        OnPropertyChanged(nameof(VetQuestionLabel));
        OnPropertyChanged(nameof(VetQuestionHeading));
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
        // definitions: every lookup a miss, so every custom card blank. The field is
        // assigned in the atomic fill instead, where the chips read it in the same breath.
        var customById = _hasPet
            ? (await _custom.GetAllForPetAsync(pet!.Id)).ToDictionary(c => c.Id)
            : new Dictionary<int, CustomTracker>();

        // The two things BOTH halves of this screen need, fetched once. The timeline
        // needs the plan (for the glucose target range) and the day's doses (for the
        // dose cards); the pending engine needs exactly the same two. They used to be
        // read twice per reload: the care plan is three queries and the dose join is
        // another three, so that was six round trips spent re-answering a question this
        // method had already asked.
        IReadOnlyList<CarePlanItem> plan = System.Array.Empty<CarePlanItem>();
        IReadOnlyList<DayDose> doses = System.Array.Empty<DayDose>();
        if (_hasPet)
        {
            plan = await _carePlan.GetPlanAsync(pet);
            doses = await _dayDoses.GetForDayAsync(pet!.Id, _date);
        }

        // Gather everything (the awaits) BEFORE touching the observable collections.
        // Several reloads fire on startup (OnAppearing + the date/pet PropertyChanged
        // handlers); if we cleared before awaiting, their clear+add would interleave
        // and duplicate the rows. The fill below is await-free, so each reload rebuilds
        // atomically on the UI thread.
        var timeline = await GatherTimelineAsync(pet, customById, plan, doses);

        IReadOnlyList<PendingItem> pending = System.Array.Empty<PendingItem>();
        if (_hasPet && IsToday)
            pending = await _pending.GetAsync(pet!, _date, plan, doses);

        // ── atomic fill: no awaits from here on ──
        // Both lists are built completely and then handed over in ONE notification.
        // The rule about not clearing before an await still holds and still matters;
        // this additionally stops the refill itself from costing a layout pass per row.
        _customById = customById;
        TimelineItems.ReplaceAll(timeline);
        RaiseTimelineFlags();

        var chips = new List<JournalChip>();
        if (_hasPet && IsToday)
        {
            BuildChips(pending, chips);
            var count = chips.Count; // real pending items, before the trailing "+"
            HasPending = count > 0;
            Heading = BuildHeading(count);
            if (HasPending)
                chips.Add(new JournalChip
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

        Chips.ReplaceAll(chips);

        NotifyStates();

        // After-write hook: a logging change today alters what's still pending, so
        // re-evaluate the daily care reminder (it may now need cancelling because the
        // day is handled, or arming). Fire-and-forget: it has its own gate and must
        // not slow the Journal reload. Only relevant for today's board.
        if (IsToday)
            _dailyReminders.RefreshAsync().Forget();
    }

    private void BuildChips(IReadOnlyList<PendingItem> pending, List<JournalChip> into)
    {
        int i = 0;
        foreach (var item in pending)
        {
            var tilt = (i++ % 2 == 0) ? -0.4 : 0.4;
            into.Add(item.Kind == PendingKind.Medication
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

    // Gather everything logged for the day as one list, then sort purely by time,
    // a single chronological ordering across every kind (§3). Legacy mood/weight rows
    // with no stored time sort at the start of the day.
    private async Task<List<TimelineItem>> GatherTimelineAsync(
        Pet? pet, IReadOnlyDictionary<int, CustomTracker> customById,
        IReadOnlyList<CarePlanItem> plan, IReadOnlyList<DayDose> doses)
    {
        var items = new List<TimelineItem>();

        if (pet == null || pet.Id == 0)
            return items;

        // One store at a time, on purpose.
        //
        // These used to be issued together under a Task.WhenAll, on the reasoning that
        // none depends on another's result. They don't, but sqlite-net's async API is
        // not asynchronous I/O: every ...Async call is queued to the THREAD POOL, where
        // it takes a lock on the one shared connection. Issuing eight at once therefore
        // occupied eight pooled threads to run one query, seven of them blocked on the
        // lock, and asked the pool to grow past its core count at exactly the moment
        // (launch, tab switch) when it is least able to. The queries ran sequentially
        // either way. Awaiting them in turn costs the same wall time and one thread.
        //
        // Each is a point lookup on a composite (PetId, Date) index: see the entry
        // models. If this ever needs to be fewer round trips, the answer is a wider
        // query, not more concurrency.
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, pet.Id);
        var glucoseEntries = await _glucose.GetForDateAsync(pet.Id, _date);
        var appetiteEntries = await _appetite.GetForDateAsync(pet.Id, _date);
        var appetiteAmounts = await _appetite.GetAmountsForDateAsync(pet.Id, _date);
        var waterAmounts = await _water.GetAmountsForDateAsync(pet.Id, _date);
        var waterLevels = await _water.GetLevelsForDateAsync(pet.Id, _date);
        var seizureEntries = await _seizures.GetForDateAsync(pet.Id, _date);
        // ONE query covering every custom tracker the pet has, however many that is,
        // grouped below. This is what keeps "as many as you like" free here.
        var customEntries = await _custom.GetForDateAsync(pet.Id, _date);

        // The treatment ledger up to the END of the day being shown. Only the dose rows
        // read it, and only when there are doses, but it is fetched here with the rest
        // because this method is where the day's reads live, and a per-dose query would
        // be one round trip per card.
        var ledger = doses.Count == 0
            ? (IReadOnlyList<MedicationChange>)System.Array.Empty<MedicationChange>()
            : await _medications.GetChangesForRangeAsync(
                pet.Id, DateTime.MinValue, _date.Date.AddDays(1).ToUniversalTime());

        // Which units this pet's readings are shown in: derived from their own entries,
        // over the whole history rather than this day, so the timeline does not relabel
        // itself as the owner pages back through it.
        var weightUnit = await _displayUnits.ResolveAsync(pet.Id, UnitFamily.Weight);
        var glucoseUnit = await _displayUnits.ResolveAsync(pet.Id, UnitFamily.Glucose);
        var durationUnit = await _displayUnits.ResolveAsync(pet.Id, UnitFamily.Duration);
        var volumeUnit = await _displayUnits.ResolveAsync(pet.Id, UnitFamily.Volume);
        var foodUnit = await _displayUnits.ResolveAsync(pet.Id, UnitFamily.FoodMass);

        // Mood + Weight (both live on the day's PetEntry, each with its own time).
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
                // The MAJORITY unit, not this row's own: the timeline is a list of what
                // happened, and a day logged in pounds sitting under a day logged in
                // kilograms would make the two look like different animals. The row's own
                // unit is still what the sheet reopens in.
                var weight = UnitText.WithUnit(entry.Weight, weightUnit);
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

        // Glucose (rose): value is precise; range sentence only when a range exists.
        //
        // The band and the readings MUST be in the same unit. The band is stored in
        // whatever the owner entered it in (Tracker.Unit) and the readings are canonical,
        // so the two can genuinely disagree: mg/dL readings beside a band still reading
        // "4-8" is a screen saying the animal is in a hypoglycaemic emergency. Both go
        // into canonical space before anything is compared, and the sentence itself is
        // built from the display unit.
        var glucoseLine = plan.FirstOrDefault(t => t.Key.Is(TrackerId.Glucose));
        var range = glucoseLine?.TargetRange;
        var rangeUnit = glucoseLine?.TargetUnit ?? UnitCatalog.Canonical(UnitFamily.Glucose);
        foreach (var g in glucoseEntries)
        {
            items.Add(new TimelineItem
            {
                Kind = TimelineKind.Glucose,
                CanDelete = true,
                EntryId = g.Id,
                Time = g.Time,
                Icon = TrackerVisuals.For(TrackerId.Glucose).Icon,
                Tint = TrackerTint(TrackerId.Glucose),
                Title = Loc.Format("Journal_GlucoseTimeline", UnitText.WithUnit(g.Value, glucoseUnit)),
                Sub = GlucoseSub(g, range, rangeUnit)
            });
        }

        // Appetite (honey): two kinds that can both appear: the day's qualitative
        // reading (Didn't eat … everything) and any exact grams events. Both may carry
        // a food label. Never judged.
        foreach (var a in appetiteEntries)
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
        foreach (var a in appetiteAmounts)
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
                Sub = WithFood(UnitText.WithUnit(a.Grams, foodUnit), a.Food)
            });
        }

        // Water (blue): two independent kinds that can both appear on a day:
        //   • exact ml readings, one card each (additive events), and
        //   • the day's single relative reading (Barely … a lot).
        // Never judged; the value is a plain fact.
        foreach (var w in waterAmounts)
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
                Sub = UnitText.WithUnit(w.AmountMl, volumeUnit)
            });
        }
        foreach (var w in waterLevels)
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

        // Seizures (violet): logged as they happen; optional duration + note.
        foreach (var s in seizureEntries)
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
                Sub = SeizureSub(s, durationUnit)
            });
        }

        // The owner's own trackers. One card per entry (they are events), wearing the
        // name, emoji and colour from the definition: including a RETIRED one, so
        // tidying the care plan never erases what was already written down.
        foreach (var c in customEntries)
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

        // Medication doses: placed at the moment they were tapped as taken/skipped
        // (their resolved time), falling back to the scheduled time when not yet acted on.
        items.AddRange(BuildDoseItems(doses, _date, ledger));

        // ── the single ordering: everything, purely by time ──
        var ordered = items.OrderBy(i => i.Time ?? TimeSpan.Zero).ToList();

        // Handmade wobble: alternate the icon tilt down the finished timeline.
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].IconRotation = i % 2 == 0 ? -3 : 2.5;

        return ordered;
    }

    // ── "Ask about this" (swipe a timeline card) ────────────────────────────────
    //
    // A vet question forms at the MOMENT you look at something that worries you: the
    // third bad mood this week, the weigh-in that dropped. Until now questions were
    // addable from the Journal's "+" and from the vet page, and neither of those is that
    // moment: by the time you have found one, the thought is a vague worry rather than
    // a question about a specific entry.
    //
    // It is a SWIPE and not a button. The row already carries a delete affordance and
    // cannot take another; a second visible control on a card someone reaches at 2am is
    // worse than a feature nobody finds.
    //
    // The prefill is ordinary editable text and nothing more. No id travels with it: a
    // question is a note to self about a conversation, and a stored link to an entry
    // would quietly turn it into a record of the animal.

    /// <summary>The page hosts the question sheet; the VM raises. Same split the chip
    /// row uses.</summary>
    public event Action<string>? AskAboutRequested;

    private void AskAbout(TimelineItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Title))
            return;

        AskAboutRequested?.Invoke(Loc.Format(
            "Vet_QuestionAbout",
            item.Title,
            _date.ToString("d MMM", CultureInfo.CurrentCulture)));
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
                    PetId = row.PetId, Date = row.Date, Time = row.Time,
                    Grams = row.Grams, Unit = row.Unit, Food = row.Food
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
                    Type = row.Type,
                    DurationSeconds = row.DurationSeconds, Unit = row.Unit, Note = row.Note
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
                    PetId = row.PetId, Date = row.Date, Time = row.Time,
                    AmountMl = row.AmountMl, Unit = row.Unit
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
                    Time = row.Time, Amount = row.Amount, Unit = row.Unit, Note = row.Note
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
                // The ✕ on a dose clears its recorded outcome (back to "still open"),
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
    /// the coarse taken/skipped bucket is sent, never the medication, dose, time, or pet.
    ///
    /// <para>Called from the three user gestures (chip tap, "Mark as given", "Mark as
    /// skipped") rather than from <see cref="MedicationDoseLogService"/>, which would also
    /// catch the reconciler stamping doses Missed on its own: a machine action, not a
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
            return; // already given: nothing to do

        var petId = pet.Id;
        var medId = item.MedicationId;
        var time = item.DoseTime;
        var date = _date;
        var prev = item.DoseOutcome;

        await _doseLogs.SetStatusAsync(medId, petId, date, time, DoseStatus.Taken);
        // A given dose is handled: don't let its reminder fire late or re-send.
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
    // Undo restores whatever the dose was before: usually "still open", occasionally
    // an earlier "given". Reuses the same refresh + undo-toast path as a delete.
    private async Task SkipDoseAsync(TimelineItem? item)
    {
        var pet = _activePet.ActivePet;
        if (item == null || !item.IsDose || !item.DoseActionable || pet == null || pet.Id == 0)
            return;
        if (item.DoseOutcome == DoseStatus.Skipped)
            return; // already skipped: nothing to do

        var petId = pet.Id;
        var medId = item.MedicationId;
        var time = item.DoseTime;
        var date = _date;
        var prev = item.DoseOutcome;

        await _doseLogs.SetStatusAsync(medId, petId, date, time, DoseStatus.Skipped);
        // A skipped dose is handled: don't let its reminder fire late or re-send.
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
    //
    // THE DOSE IS RESOLVED FROM THE LEDGER, NEVER FROM THE MEDICATION ROW. Reading
    // d.Medication.Dosage here meant that raising ProZinc from 2 IU to 3 IU today
    // silently restated the 19 August entry as "3 IU · Given": a false statement about
    // a dose that was given at 2 IU, sitting in a medical record, produced by an edit
    // made somewhere else entirely. This is the exact failure the treatment ledger was
    // built to end, and MedicationChange rows are what finally make it fixable.
    //
    // The moment compared against is the dose's OWN moment (resolved, else scheduled),
    // not the day: a change made at 09:00 must not rewrite the 08:00 dose beneath it.
    private static List<TimelineItem> BuildDoseItems(
        IReadOnlyList<DayDose> doses, DateTime date, IReadOnlyList<MedicationChange> changes)
    {
        var now = DateTime.Now;
        var honey = Tint("HoneyWarmTint");
        var result = new List<TimelineItem>();

        foreach (var d in doses)
        {
            var canToggle = date < now.Date || (date == now.Date && d.ScheduledTime <= now.TimeOfDay);
            var outcome = d.Log?.Status;
            var at = d.Log?.ResolvedAt?.TimeOfDay ?? d.ScheduledTime;

            // Null means the ledger has nothing about this medication before that moment
            // pre-ledger history. Falling back to the current row is honest there: we
            // genuinely do not know, and the current number is the only one we have.
            var dose = MedicationLedger.DoseAsOf(changes, d.Medication.Id, (date.Date + at).ToUniversalTime())
                ?? $"{d.Medication.Dosage} {d.Medication.Unit}";

            result.Add(new TimelineItem
            {
                Kind = TimelineKind.Dose,
                Time = at,
                Icon = "💊",
                Tint = honey,
                Title = d.Medication.Name,
                Sub = $"{dose} · {DoseStatusText(d.Log, canToggle)}",
                MedicationId = d.Medication.Id,
                DoseTime = d.ScheduledTime,
                DoseOutcome = outcome,
                DoseActionable = canToggle,
                // The ✕ clears a recorded outcome, so only offer it once one exists
                // (a dose still "open" has nothing to undo: it's a chip, not a delete).
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
    private static string SeizureSub(SeizureEntry s, UnitDef durationUnit)
    {
        var parts = new List<string>(3);
        if (s.Type is SeizureType t)
            parts.Add(t.GetDisplayName());
        if (s.DurationSeconds is int seconds)
            parts.Add(Loc.Format(
                "Journal_SeizureDuration", UnitText.WithUnit(seconds, durationUnit)));
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

    // "{Before|After} food": plus a gentle range sentence ONLY when a range exists.
    // The value itself is never coloured or altered by the range.
    //
    // THE COMPARISON HAPPENS IN CANONICAL SPACE. The reading is stored in mmol/L and the
    // band in whatever unit it was typed in, so comparing them raw was already a latent
    // bug: it was right only because every band in the world was mmol/L. With mg/dL
    // offered, a 4-8 band read as 4-8 mmol/L against a reading of 7.6 says "in range",
    // and the same band entered as 72-144 mg/dL would have said "low" about the identical
    // reading. One of those two answers is a false statement about an animal.
    private static string GlucoseSub(GlucoseEntry g, TargetRange? range, UnitDef rangeUnit)
    {
        var context = Loc.GetString(g.Context == FoodContext.BeforeFood ? "Journal_BeforeFood" : "Journal_AfterFood");
        if (range is not { } r)
            return context;

        var canonical = UnitCatalog.CanonicalRange(r, rangeUnit);
        string sentence = Loc.GetString(
            canonical.Contains(g.Value) ? "Journal_GlucoseInRange"
            : g.Value > canonical.Hi ? "Journal_GlucoseHigh"
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
    // deliberately not changed, that one is a to-do list, and putting unopted-in
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

    /// <summary>
    /// The "+" sheet's third block: one row, on its own, for something that is not a log
    /// type at all.
    ///
    /// <para>It sits apart from both groups deliberately. <c>AddOptions</c> is the pet's
    /// plan and <c>MoreOptions</c> is "everything else the app can record": a question
    /// is neither, because it records nothing about the animal. Putting it in either
    /// list would make it read as a tracker, which is the one thing it must never
    /// become.</para>
    /// </summary>
    public AddOption VetQuestionOption { get; } = new()
    {
        Kind = JournalChipKind.VetQuestion,
        // A note about a conversation, not a record: hence the speech balloon rather
        // than any of the tracker icons.
        Icon = "\U0001F4AC",
        Label = string.Empty,   // resolved live by the view; see VetQuestionLabel
    };

    /// <summary>Resolved per read, never cached: this VM is a singleton and a cached
    /// string would freeze in the language active at construction.</summary>
    public string VetQuestionLabel => Loc.GetString("Vet_QuestionRow");

    /// <summary>The heading the row sits under: it names the occasion rather than the
    /// person, which is what a question-for-the-visit actually belongs to.</summary>
    public string VetQuestionHeading => Loc.GetString("Vet_QuestionSection");

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
        // else the app can also record". Retired ones are absent: they were retired to
        // stop being offered, which is why this reads the live list, not _customById.
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
