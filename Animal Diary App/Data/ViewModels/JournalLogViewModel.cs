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

public enum JournalChipKind { Medication, Glucose, Mood, Appetite, Weight, Seizure, Add }

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

/// <summary>A completed glucose reading on the timeline. The value is precise; the
/// range sentence is added only when the tracker actually has a target range.</summary>
public class GlucoseTimelineItem
{
    public string TimeDisplay { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Sub { get; init; } = string.Empty;
}

/// <summary>A completed appetite reading on the timeline ("Ate most of it").</summary>
public class AppetiteTimelineItem
{
    public string TimeDisplay { get; init; } = string.Empty;
    public string Sub { get; init; } = string.Empty;
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
    private readonly MedicationDoseLogService _doseLogs;
    private readonly MedicationReminderScheduler _reminders;
    private readonly GlucoseEntryService _glucose;
    private readonly AppetiteEntryService _appetite;

    private DateTime _date = DateTime.Now.Date;

    public JournalLogViewModel(
        PendingItemsService pending,
        CarePlanService carePlan,
        ActivePetService activePet,
        MedicationDoseLogService doseLogs,
        MedicationReminderScheduler reminders,
        GlucoseEntryService glucose,
        AppetiteEntryService appetite)
    {
        _pending = pending;
        _carePlan = carePlan;
        _activePet = activePet;
        _doseLogs = doseLogs;
        _reminders = reminders;
        _glucose = glucose;
        _appetite = appetite;

        OpenAddSheetCommand = new Command(async () => await OpenAddSheetAsync());
        CloseAddSheetCommand = new Command(() => IsAddSheetVisible = false);
        SelectAddOptionCommand = new Command<AddOption>(OnSelectAddOption);
    }

    // Short-hand for the localized string manager (usable from the static chip builders).
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>Raised when the person picks a log type (from a chip or the "+"
    /// sheet). The page opens the matching sheet — it owns the sheet VMs + animations.</summary>
    public event Action<JournalChipKind>? RequestOpenSheet;

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

    // ── Timeline (glucose + appetite; mood/weight/doses stay on CalendarViewModel) ─
    public ObservableCollection<GlucoseTimelineItem> GlucoseItems { get; } = new();
    public ObservableCollection<AppetiteTimelineItem> AppetiteItems { get; } = new();

    public bool HasGlucoseItems => GlucoseItems.Count > 0;
    public bool HasAppetiteItems => AppetiteItems.Count > 0;

    // ── Add-anything sheet ───────────────────────────────────────────────────────
    public ObservableCollection<AddOption> AddOptions { get; } = new();

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
        var (glucose, appetite) = await GatherTimelineAsync(pet);

        IReadOnlyList<PendingItem> pending = System.Array.Empty<PendingItem>();
        if (_hasPet && IsToday)
            pending = await _pending.GetAsync(pet!, _date);

        // ── atomic fill: no awaits from here on ──
        GlucoseItems.Clear();
        foreach (var g in glucose) GlucoseItems.Add(g);
        AppetiteItems.Clear();
        foreach (var a in appetite) AppetiteItems.Add(a);
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
            // Water has no logging sheet yet (TODO: a later task adds it). Skip it so
            // it never surfaces as a dead-end chip; every other pending item has a sheet.
            if (item.Kind == PendingKind.Tracker && item.TrackerId == TrackerId.Water)
                continue;

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
        TrackerId.Water => Simple(JournalChipKind.Weight, "💧", Loc.GetString("Journal_Water"), tilt, Loc.GetString("Journal_A11yLogWater")), // no sheet yet; filtered in BuildChips
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

    private async Task<(List<GlucoseTimelineItem> glucose, List<AppetiteTimelineItem> appetite)> GatherTimelineAsync(Pet? pet)
    {
        var glucose = new List<GlucoseTimelineItem>();
        var appetite = new List<AppetiteTimelineItem>();

        if (pet == null || pet.Id == 0)
            return (glucose, appetite);

        var range = (await _carePlan.GetPlanAsync(pet))
            .FirstOrDefault(t => t.TrackerId == TrackerId.Glucose)?.TargetRange;

        foreach (var g in await _glucose.GetForDateAsync(pet.Id, _date))
        {
            glucose.Add(new GlucoseTimelineItem
            {
                TimeDisplay = g.Time.ToString(@"hh\:mm"),
                Title = Loc.Format("Journal_GlucoseTimeline", g.Value.ToString("0.0", CultureInfo.CurrentCulture)),
                Sub = GlucoseSub(g, range)
            });
        }

        foreach (var a in await _appetite.GetForDateAsync(pet.Id, _date))
        {
            var word = ((AppetiteLevel)a.Level).GetDisplayName().ToLowerInvariant();
            appetite.Add(new AppetiteTimelineItem
            {
                TimeDisplay = a.Time.ToString(@"hh\:mm"),
                Sub = Loc.Format("Journal_AteWord", word)
            });
        }

        return (glucose, appetite);
    }

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
            () => _doseLogs.ClearStatusAsync(medId, date, time));
    }

    // ── Add-anything options (tracker sheets present in the plan) ──────────────────
    private async Task BuildAddOptionsAsync()
    {
        AddOptions.Clear();
        var pet = _activePet.ActivePet;
        if (pet == null)
            return;

        var plan = await _carePlan.GetPlanAsync(pet);
        bool Has(TrackerId id) => plan.Any(t => t.TrackerId == id);

        if (Has(TrackerId.Glucose)) AddOptions.Add(new AddOption { Kind = JournalChipKind.Glucose, Icon = "🩸", Label = Loc.GetString("Journal_GlucoseCheck") });
        AddOptions.Add(new AddOption { Kind = JournalChipKind.Mood, Icon = "🙂", Label = Loc.GetString("Journal_MoodTitle") });
        if (Has(TrackerId.Appetite)) AddOptions.Add(new AddOption { Kind = JournalChipKind.Appetite, Icon = "🍽️", Label = Loc.GetString("Journal_Appetite") });
        AddOptions.Add(new AddOption { Kind = JournalChipKind.Weight, Icon = "⚖️", Label = Loc.GetString("Journal_WeighIn") });
        if (Has(TrackerId.Seizure)) AddOptions.Add(new AddOption { Kind = JournalChipKind.Seizure, Icon = "⚡", Label = Loc.GetString("Journal_Seizure") });
    }

    private void NotifyStates()
    {
        OnPropertyChanged(nameof(ShowAllDone));
        OnPropertyChanged(nameof(ShowStillToDo));
        OnPropertyChanged(nameof(ShowPastAdd));
    }

    private void RaiseTimelineFlags()
    {
        OnPropertyChanged(nameof(HasGlucoseItems));
        OnPropertyChanged(nameof(HasAppetiteItems));
    }
}
