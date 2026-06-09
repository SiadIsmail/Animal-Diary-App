namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.Globalization;
using System.Windows.Input;
using System.Collections.ObjectModel;
using SQLite;

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

    // Drawing area height (in device-independent units) the bars are scaled into.
    public const double ChartHeight = 140;
    // Smallest visible bar so the lowest entry is never a flat line.
    private const double MinBarHeight = 14;

    private bool hasSufficientWeightData;
    public bool HasSufficientWeightData
    {
        get => hasSufficientWeightData;
        set => SetProperty(ref hasSufficientWeightData, value);
    }

    private PetEntry? EntryToday;
    public async Task LoadLatestWeightAsync()
    {
        EntryToday = await _petEntryService.GetPetEntriesAsync().ContinueWith(t => t.Result.OrderByDescending(e => e.Date).FirstOrDefault());
        if (EntryToday != null)
        {
            LatestWeight = EntryToday.Weight;
        }
    }

    public async Task LoadWeightChartAsync()
    {
        if (ActivePet == null) return;

        WeightChartData.Clear();
        var entries = await _petEntryService.GetLast30DaysWeightEntriesAsync(ActivePet.Id);

        // Normalize bar heights against the data range so even small
        // weight differences are clearly visible in the chart.
        decimal axisMin = 0, axisMax = 0;
        if (entries.Count > 0)
        {
            axisMin = Math.Floor(entries.Min(e => e.Weight));
            axisMax = Math.Ceiling(entries.Max(e => e.Weight));
            if (axisMax <= axisMin) axisMax = axisMin + 1;
        }

        foreach (var entry in entries)
        {
            double fraction = (double)((entry.Weight - axisMin) / (axisMax - axisMin));
            double barHeight = MinBarHeight + fraction * (ChartHeight - MinBarHeight);

            WeightChartData.Add(new ChartDataPoint
            {
                Date = entry.Date,
                Value = entry.Weight,
                BarHeight = barHeight
            });
        }

        HasSufficientWeightData = entries.Count >= 2;
    }

    public async Task LoadMoodTimelineAsync()
    {
        if (ActivePet == null) return;
        await MoodTimeline.LoadLast30DaysAsync(ActivePet.Id);
    }

    public ICommand NavigateToMoodDateCommand => new Command<DateTime>(async date =>
    {
        // This command would be used to navigate to the calendar on a specific date if needed
        Console.WriteLine($"Tapped mood date: {date:M/d/yyyy}");
    });
}