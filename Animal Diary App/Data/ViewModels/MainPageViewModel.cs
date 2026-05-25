namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.Globalization;
using System.Windows.Input;
using SQLite;

public class MainPageViewModel : BaseViewModel
{
    public List<PetEntry> Entries { get; set; } = new();
    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly SettingsService _SettingsService;

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        set => _activePetService.ActivePet = value;
    }

    public MainPageViewModel(PetEntryService petEntryService, PetService petService, ActivePetService activePetService, SettingsService settingsService)
    {
        _petEntryService = petEntryService;
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;

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
    private PetEntry? EntryToday;
    public async Task LoadLatestWeightAsync()
    {
        EntryToday = await _petEntryService.GetPetEntriesAsync().ContinueWith(t => t.Result.OrderByDescending(e => e.Date).FirstOrDefault());
        if (EntryToday != null)
        {
            LatestWeight = EntryToday.Weight;
        }
    }
    public async Task LoadCurrentPet()
    {
        ActivePet = await _petService.GetPetByIdAsync(1);
    }

}