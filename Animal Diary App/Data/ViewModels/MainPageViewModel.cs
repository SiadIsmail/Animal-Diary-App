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
    }

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

    // Signed change across the visible range, e.g. "+0.3" / "−0.2" / "±0.0".
    private string weightTrendLabel = string.Empty;
    public string WeightTrendLabel { get => weightTrendLabel; set => SetProperty(ref weightTrendLabel, value); }

    // True when the change is negligible → chip reads as calm/sage rather than honey.
    private bool weightTrendIsStable = true;
    public bool WeightTrendIsStable { get => weightTrendIsStable; set => SetProperty(ref weightTrendIsStable, value); }

    private PetEntry? EntryToday;
    public async Task LoadLatestWeightAsync()
    {
        if (ActivePet == null) return;
        EntryToday = await _petEntryService.GetLatestWeightEntryAsync(ActivePet.Id);
        if (EntryToday != null)
        {
            LatestWeight = EntryToday.Weight;
        }
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

            var diff = latest - entries[0].Weight;
            WeightTrendIsStable = Math.Abs(diff) < 0.05m;
            var sign = WeightTrendIsStable ? "±" : diff > 0 ? "+" : "−";
            WeightTrendLabel = $"{sign}{Math.Abs(diff).ToString("0.0")} kg";
        }
        else
        {
            CurrentWeightLabel = "—";
            WeightTrendLabel = string.Empty;
            WeightAxisMin = 0;
            WeightAxisMax = 1;
        }

        HasSufficientWeightData = entries.Count >= 2;
        WeightChartUpdated?.Invoke();
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