namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
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

    public MoodTimelineViewModel MoodTimeline { get; }

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        set => _activePetService.ActivePet = value;
    }

    public MainPageViewModel(PetEntryService petEntryService, PetService petService, ActivePetService activePetService, SettingsService settingsService, MoodTimelineViewModel moodTimeline)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;
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
}