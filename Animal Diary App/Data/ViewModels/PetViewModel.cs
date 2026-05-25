namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

public class PetViewModel : BaseViewModel
{
    private string enteredPetName = string.Empty;

    public string EnteredPetName
    {
        get => enteredPetName;
        set => SetProperty(ref enteredPetName, value);
    }

    private string enteredPetType = string.Empty;

    public string EnteredPetType
    {
        get => enteredPetType;
        set => SetProperty(ref enteredPetType, value);
    }

    private string enteredPetAge = string.Empty;

    public string EnteredPetAge
    {
        get => enteredPetAge;
        set
        {
            if (SetProperty(ref enteredPetAge, value))
            {
                OnPropertyChanged(nameof(ParsedPetAge));
            }
        }
    }

    private PetTypeOption? selectedPetType;
    public PetTypeOption? SelectedPetType
    {
        get => selectedPetType;
        set
        {
            if (SetProperty(ref selectedPetType, value))
            {
                UpdatePetTypeSelection();
            }
        }
    }

    public ObservableCollection<PetTypeOption> PetTypeOptions { get; set; } = new();

    private bool showCustomPetType;
    public bool ShowCustomPetType
    {
        get => showCustomPetType;
        set => SetProperty(ref showCustomPetType, value);
    }

    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;

    public ObservableCollection<Pet> Pets { get; set; } = new ObservableCollection<Pet>();

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        private set => _activePetService.ActivePet = value;
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

    public PetViewModel(PetService petService, ActivePetService activePetService)
    {
        _petService = petService;
        _activePetService = activePetService;
        SelectPetCommand = new Command<Pet>(SelectPet);
        AddPetCommand = new Command(() => Console.WriteLine("AddPetCommand executed"));
        OpenActivePetProfileCommand = new Command(() => Console.WriteLine("Open active pet profile"));
        EditActivePetCommand = new Command(() => Console.WriteLine($"Edit pet {ActivePet.Name}"));
        OpenDocumentsCommand = new Command(() => Console.WriteLine("Open documents"));
        ExportReportCommand = new Command(() => Console.WriteLine("Export report"));
        OpenMedicationDetailCommand = new Command(() => Console.WriteLine("Open medication details"));

        InitializePetTypeOptions();

        _activePetService.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ActivePet))
            {
                OnPropertyChanged(nameof(ActivePet));
                OnPropertyChanged(nameof(ActivePetEmoji));
                OnPropertyChanged(nameof(ActivePetSubtitle));
            }
        };
    }

    private void InitializePetTypeOptions()
    {
        PetTypeOptions.Clear();
        PetTypeOptions.Add(new PetTypeOption { Name = "Dog", Emoji = "🐶" });
        PetTypeOptions.Add(new PetTypeOption { Name = "Cat", Emoji = "🐱" });
        PetTypeOptions.Add(new PetTypeOption { Name = "Bird", Emoji = "🐦" });
        PetTypeOptions.Add(new PetTypeOption { Name = "Rabbit", Emoji = "🐰" });
        PetTypeOptions.Add(new PetTypeOption { Name = "Fish", Emoji = "🐠" });
        PetTypeOptions.Add(new PetTypeOption { Name = "Other", Emoji = "🐾" });
    }

    private void UpdatePetTypeSelection()
    {
        foreach (var petType in PetTypeOptions)
        {
            petType.IsSelected = petType == SelectedPetType;
        }

        ShowCustomPetType = SelectedPetType?.Name == "Other";

        if (SelectedPetType != null && SelectedPetType.Name != "Other")
        {
            EnteredPetType = SelectedPetType.Name;
        }
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
            var savedPetId = await _activePetService.GetSavedActivePetIdAsync();
            var petToSelect = Pets.FirstOrDefault(p => p.Id == savedPetId) ?? Pets[0];
            SelectPet(petToSelect);
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
}