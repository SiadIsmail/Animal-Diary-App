namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;
using System.Windows.Input;
using System.Collections.ObjectModel;

public class MainPageViewModel : BaseViewModel
{
    public List<PetEntry> Entries { get; set; } = new();
    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly SettingsService _SettingsService;
    private readonly PendingItemsService _pendingItems;
    private readonly MedicationService _medicationService;
    private readonly MedicationDoseLogService _doseLogService;
    private readonly MedicationReminderScheduler _reminderScheduler;

    public MoodTimelineViewModel MoodTimeline { get; }

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        set => _activePetService.ActivePet = value;
    }

    public MainPageViewModel(PetEntryService petEntryService, PetService petService, ActivePetService activePetService, SettingsService settingsService, MoodTimelineViewModel moodTimeline,
        PendingItemsService pendingItems, MedicationService medicationService, MedicationDoseLogService doseLogService, MedicationReminderScheduler reminderScheduler)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;
        _pendingItems = pendingItems;
        _medicationService = medicationService;
        _doseLogService = doseLogService;
        _reminderScheduler = reminderScheduler;
        MoodTimeline = moodTimeline;

        // Commands are created once — an expression-bodied `=> new Command(...)`
        // property hands out a fresh instance per read, which allocates on every
        // binding access and can never support CanExecuteChanged.
        SetChartRangeCommand = new Command<string>(days =>
        {
            if (int.TryParse(days, out var d))
                ChartRangeDays = d;
        });
        NavigateToMoodDateCommand = new Command<DateTime>(date =>
        {
            // Stub: would navigate the Journal to this date.
            System.Diagnostics.Debug.WriteLine($"Tapped mood date: {date:M/d/yyyy}");
        });

        _activePetService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ActivePet))
            {
                OnPropertyChanged(nameof(ActivePet));
            }
        };

        // This VM is a singleton, so a greeting cached here would survive a live
        // language switch in the old language. Re-raise instead and let the getter
        // resolve the string fresh (the DaySelectionItem pattern).
        LocalizationManager.Instance.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Greeting));
            OnPropertyChanged(nameof(WeightTrendLabel));
            OnPropertyChanged(nameof(LatestMoodLabel));
            OnPropertyChanged(nameof(LatestMoodLoggedLabel));
            OnPropertyChanged(nameof(LatestWeightLoggedLabel));
        };
    }

    /// <summary>Time-of-day greeting on the Today header. Resolved per read — the
    /// hour can roll over while the page is alive, and the string must follow the
    /// active language. Refreshed on every appearance via <see cref="LoadTodayCareAsync"/>.</summary>
    public string Greeting => LocalizationManager.Instance.GetString(
        DateTime.Now.Hour switch
        {
            < 12 => "Main_GreetingMorning",
            < 18 => "Main_GreetingAfternoon",
            _ => "Main_GreetingEvening"
        });

    private decimal latestWeight;
    public decimal LatestWeight
    {
        get => latestWeight;
        set => SetProperty(ref latestWeight, value);
    }

    public ObservableCollection<ChartDataPoint> WeightChartData { get; } = new();

    /// <summary>Raised after the weight series is reloaded so the chart surface
    /// (a GraphicsView) can pull fresh values and invalidate itself.</summary>
    public event Action? WeightChartUpdated;

    private bool hasSufficientWeightData;
    public bool HasSufficientWeightData
    {
        get => hasSufficientWeightData;
        set => SetProperty(ref hasSufficientWeightData, value);
    }

    // ── Weight-trend chart state ─────────────────────────────────────────
    // Selected range in days: 14 (2W) · 30 (1M) · 90 (3M) · 3650 (All).
    private int chartRangeDays = 30;
    public int ChartRangeDays
    {
        get => chartRangeDays;
        set
        {
            if (SetProperty(ref chartRangeDays, value))
                LoadWeightChartAsync().Forget();
        }
    }

    public ICommand SetChartRangeCommand { get; }

    // Padded value axis the chart normalizes points into.
    private double weightAxisMin;
    public double WeightAxisMin { get => weightAxisMin; set => SetProperty(ref weightAxisMin, value); }

    private double weightAxisMax = 1;
    public double WeightAxisMax { get => weightAxisMax; set => SetProperty(ref weightAxisMax, value); }

    // Most-recent weight, shown big above the chart.
    private string currentWeightLabel = "—";
    public string CurrentWeightLabel { get => currentWeightLabel; set => SetProperty(ref currentWeightLabel, value); }

    // Signed change across the visible range; null when the range holds no readings.
    private decimal? weightDiff;

    /// <summary>True when the change across the range is negligible.</summary>
    public bool WeightTrendIsStable => weightDiff is null || Math.Abs(weightDiff.Value) < 0.05m;

    /// <summary>The trend chip's text. A real change is stated as a signed value
    /// ("+0.3 kg") — a fact, never coloured or worded as good or bad, because the app
    /// cannot know which a given pet's change is. A negligible change reads as a plain
    /// sentence instead of a hollow "±0.0 kg". Resolved per read so a live language
    /// switch re-translates it (this VM is a singleton).</summary>
    public string WeightTrendLabel
    {
        get
        {
            if (weightDiff is null)
                return string.Empty;

            if (WeightTrendIsStable)
                return LocalizationManager.Instance.GetString("Main_WeightStable");

            var sign = weightDiff.Value > 0 ? "+" : "−";
            return $"{sign}{Math.Abs(weightDiff.Value).ToString("0.0")} kg";
        }
    }

    private PetEntry? EntryToday;
    private DateTime? latestWeightDate;

    /// <summary>False until the pet has at least one weigh-in; drives the card's swap
    /// between the reading and its empty state.</summary>
    public bool HasLoggedWeight => latestWeightDate is not null;

    /// <summary>"Logged today" / "Logged yesterday" / "Logged 5 days ago" for the most
    /// recent weigh-in — when it was taken, not a verdict on what it says.</summary>
    public string LatestWeightLoggedLabel => RelativeLoggedLabel(latestWeightDate);

    public async Task LoadLatestWeightAsync()
    {
        if (ActivePet == null) return;

        EntryToday = await _petEntryService.GetLatestWeightEntryAsync(ActivePet.Id);
        // Clear on a pet with no weigh-ins too, or the previous pet's value lingers.
        LatestWeight = EntryToday?.Weight ?? 0;
        latestWeightDate = EntryToday?.Date;

        OnPropertyChanged(nameof(HasLoggedWeight));
        OnPropertyChanged(nameof(LatestWeightLoggedLabel));
    }

    /// <summary>Shared by both stat cards: "Logged {today|yesterday|N days ago}",
    /// reusing the Journal's relative-date vocabulary. Empty when never logged.</summary>
    private static string RelativeLoggedLabel(DateTime? date)
    {
        if (date is null)
            return string.Empty;

        var loc = LocalizationManager.Instance;
        int diff = (DateTime.Now.Date - date.Value.Date).Days;
        var relative = diff switch
        {
            <= 0 => loc.GetString("Journal_RelToday"),
            1 => loc.GetString("Journal_RelYesterday"),
            _ => loc.Format("Journal_RelDaysAgo", diff),
        };
        return loc.Format("Main_LoggedRelative", relative);
    }

    public async Task LoadWeightChartAsync()
    {
        if (ActivePet == null) return;

        WeightChartData.Clear();
        var entries = await _petEntryService.GetWeightEntriesForRangeAsync(ActivePet.Id, ChartRangeDays);

        foreach (var entry in entries)
        {
            WeightChartData.Add(new ChartDataPoint
            {
                Date = entry.Date,
                Value = entry.Weight
            });
        }

        if (entries.Count > 0)
        {
            var min = entries.Min(e => e.Weight);
            var max = entries.Max(e => e.Weight);

            // Pad the axis a little so the line never hugs the top/bottom edge,
            // and so a perfectly flat series still renders as a centered line.
            var pad = (max - min) * 0.15m;
            if (pad < 0.1m) pad = 0.2m;
            WeightAxisMin = (double)(min - pad);
            WeightAxisMax = (double)(max + pad);

            var latest = entries[^1].Weight;
            CurrentWeightLabel = latest.ToString("0.0");

            weightDiff = latest - entries[0].Weight;
        }
        else
        {
            CurrentWeightLabel = "—";
            weightDiff = null;
            WeightAxisMin = 0;
            WeightAxisMax = 1;
        }

        OnPropertyChanged(nameof(WeightTrendIsStable));
        OnPropertyChanged(nameof(WeightTrendLabel));

        HasSufficientWeightData = entries.Count >= 2;
        WeightChartUpdated?.Invoke();
    }

    // ── Mood stat card: the pet's most recent mood, however old ─────────
    // Same rule as the weight card beside it — state the latest reading and when
    // it was taken. No cutoff, no interpretation.

    private MoodLevel latestMood = MoodLevel.None;
    private DateTime? latestMoodDate;

    /// <summary>False until the pet has at least one mood logged; drives the card's
    /// swap between the reading and its empty state.</summary>
    public bool HasLoggedMood => latestMoodDate is not null;

    /// <summary>Localized word for the latest mood ("Great"), empty when none.</summary>
    public string LatestMoodLabel => HasLoggedMood ? latestMood.GetDisplayName() : string.Empty;

    /// <summary>Swatch colour for the latest mood. A VM-held presentation hint, the
    /// same accepted convention TimelineItem uses.</summary>
    public Color LatestMoodColor => latestMood.GetColor();

    /// <summary>"Logged today" / "Logged yesterday" / "Logged 5 days ago" for the most
    /// recent mood.</summary>
    public string LatestMoodLoggedLabel => RelativeLoggedLabel(latestMoodDate);

    public async Task LoadLatestMoodAsync()
    {
        if (ActivePet == null)
            return;

        var entry = await _petEntryService.GetLatestMoodEntryAsync(ActivePet.Id);
        latestMood = entry is null ? MoodLevel.None : (MoodLevel)entry.MoodLevel;
        latestMoodDate = entry?.Date;

        OnPropertyChanged(nameof(HasLoggedMood));
        OnPropertyChanged(nameof(LatestMoodLabel));
        OnPropertyChanged(nameof(LatestMoodColor));
        OnPropertyChanged(nameof(LatestMoodLoggedLabel));
    }

    public async Task LoadMoodTimelineAsync()
    {
        if (ActivePet == null) return;
        await MoodTimeline.LoadLast30DaysAsync(ActivePet.Id);
    }

    public ICommand NavigateToMoodDateCommand { get; }

    // ── Today's care: the avatar ring + next-up card ─────────────────────
    // Both are derived from the SAME PendingEngine snapshot the Journal's
    // "Still to do" chips use — always evaluated for TODAY and the active pet,
    // never for the Journal's parked date selection.

    private double careProgress;
    /// <summary>Fraction of today's care done (0..1); drives the avatar ring.
    /// 0 when nothing is scheduled or tracked at all.</summary>
    public double CareProgress
    {
        get => careProgress;
        set => SetProperty(ref careProgress, value);
    }

    /// <summary>The single next thing to do today — pending med doses first
    /// (soonest due), then care-plan trackers — or null when everything's done.
    /// Consumed by MainPage's next-up card from code-behind.</summary>
    public PendingItem? NextUpItem { get; private set; }

    /// <summary>Dosage line for a medication next-up ("2 mg"); empty for trackers.</summary>
    public string NextUpDetail { get; private set; } = string.Empty;

    public async Task LoadTodayCareAsync()
    {
        // Cheap, and this runs on every appearance — so a page left open across
        // noon or 6pm picks up the right greeting when it comes back.
        OnPropertyChanged(nameof(Greeting));

        if (ActivePet == null)
        {
            CareProgress = 0;
            NextUpItem = null;
            NextUpDetail = string.Empty;
            return;
        }

        var care = await _pendingItems.GetTodayCareAsync(ActivePet, DateTime.Now.Date);
        CareProgress = care.Progress.Total == 0 ? 0 : (double)care.Progress.Done / care.Progress.Total;
        NextUpItem = care.Pending.FirstOrDefault();

        // The pending item carries only the med's identity; the card also shows
        // the dosage, which lives on the medication row.
        NextUpDetail = string.Empty;
        if (NextUpItem is { Kind: PendingKind.Medication } dose)
        {
            var med = await _medicationService.GetMedicationByIdAsync(dose.MedicationId);
            if (med != null)
                NextUpDetail = $"{med.Dosage} {med.Unit}";
        }
    }

    /// <summary>Record the current next-up medication dose as taken (the card's
    /// one-tap action), then re-derive the ring + next-up. Same dose-log write and
    /// reminder bookkeeping as the Journal's chip tap.</summary>
    public async Task MarkNextDoseGivenAsync()
    {
        if (NextUpItem is not { Kind: PendingKind.Medication } dose)
            return;

        var today = DateTime.Now.Date;
        var time = dose.DoseTime ?? TimeSpan.Zero;
        await _doseLogService.SetStatusAsync(dose.MedicationId, dose.PetId, today, time, DoseStatus.Taken);
        // Stop this occurrence's reminder from firing late or being re-sent.
        await _reminderScheduler.MarkDoseHandledAsync(dose.MedicationId, today, time);

        await LoadTodayCareAsync();
    }
}