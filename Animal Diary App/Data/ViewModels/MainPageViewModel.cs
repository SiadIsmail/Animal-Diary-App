namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Windows.Input;
using SQLite;

public class MainPageViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    public List<PetEntry> Entries { get; set; } = new();
    private readonly PetEntryService _petEntryService;
    private readonly PetService _petService;
    private Pet activePet;
    public Pet ActivePet
    {
        get => activePet;
        set
        {
            if (activePet == value) return;
            activePet = value;
            OnPropertyChanged();
        }
    }

    public MainPageViewModel(PetEntryService petEntryService, PetService petService)
    {
        _petEntryService = petEntryService;
        _petService = petService;
    }

    private decimal latestWeight;
    public decimal LatestWeight
    {
        get => latestWeight;
        set
        {
            if (latestWeight == value) return;
            latestWeight = value;
            OnPropertyChanged();
        }
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