namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

public class PetViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string enteredPetName = string.Empty;

    public string EnteredPetName
    {
        get => enteredPetName;
        set
        {
            if (enteredPetName == value)
            {
                return;
            }

            enteredPetName = value;
            OnPropertyChanged();
        }
    }

    private string enteredPetType = string.Empty;

    public string EnteredPetType
    {
        get => enteredPetType;
        set
        {
            if (enteredPetType == value)
            {
                return;
            }

            enteredPetType = value;
            OnPropertyChanged();
        }
    }

    private string enteredPetAge = string.Empty;

    public string EnteredPetAge
    {
        get => enteredPetAge;
        set
        {
            if (enteredPetAge == value)
            {
                return;
            }

            enteredPetAge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParsedPetAge));
        }
    }

    private readonly PetService _petService;

    public ObservableCollection<Pet> Pets { get; set; } = new ObservableCollection<Pet>();

    private Pet activePet = new Pet();
    public Pet ActivePet
    {
        get => activePet;
        private set
        {
            if (activePet == value)
                return;

            activePet = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActivePetEmoji));
            OnPropertyChanged(nameof(ActivePetSubtitle));
        }
    }

    public string ActivePetEmoji => GetEmojiForType(ActivePet.Type);

    public string ActivePetSubtitle => ActivePet == null
        ? string.Empty
        : $"{ActivePet.Type} · {ActivePet.Age} yrs";

    public ICommand SelectPetCommand { get; }
    public ICommand AddPetCommand { get; }
    public ICommand OpenActivePetProfileCommand { get; }
    public ICommand EditActivePetCommand { get; }
    public ICommand OpenDocumentsCommand { get; }
    public ICommand ExportReportCommand { get; }
    public ICommand OpenMedicationDetailCommand { get; }

    public PetViewModel(PetService petService)
    {
        _petService = petService;
        SelectPetCommand = new Command<Pet>(SelectPet);
        AddPetCommand = new Command(() => Console.WriteLine("AddPetCommand executed"));
        EditActivePetCommand = new Command(() => Console.WriteLine($"Edit pet {ActivePet.Name}"));
        OpenDocumentsCommand = new Command(() => Console.WriteLine("Open documents"));
        ExportReportCommand = new Command(() => Console.WriteLine("Export report"));
    }
    
    public async Task SavePetAsync()
    {
        if (ParsedPetAge == null)
            return;

        var pet = new Pet
        {
            Name = EnteredPetName,
            Type = EnteredPetType,
            Age = ParsedPetAge.Value
        };

        await _petService.SavePetAsync(pet);
        Pets.Add(pet);
        SelectPet(pet);
    }

    public async Task LoadPetsAsync()
    {
        var allPets = await _petService.GetPetsAsync();

        Pets.Clear();
        foreach (var pet in allPets)
        {
            Pets.Add(pet);
        }

        if (Pets.Count > 0)
        {
            SelectPet(Pets[0]);
        }
    }

    private void SelectPet(Pet pet)
    {
        if (pet == null)
            return;

        foreach (var p in Pets)
        {
            p.IsSelected = false;
        }

        pet.IsSelected = true;
        ActivePet = pet;
    }

    public int? ParsedPetAge =>
        int.TryParse(EnteredPetAge, out var age) ? age : null;

    private string GetEmojiForType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return "🐾";

        return type.ToLowerInvariant() switch
        {
            "dog" => "🐶",
            "cat" => "🐱",
            "bird" => "🐦",
            "rabbit" => "🐰",
            "fish" => "🐠",
            _ => "🐾",
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}