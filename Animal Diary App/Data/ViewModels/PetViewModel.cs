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
        set
        {
            if (SetProperty(ref enteredPetName, value))
            {
                OnPropertyChanged(nameof(CanSavePet));
                OnPropertyChanged(nameof(PreviewName));
                ValidatePetName();
            }
        }
    }

    private string enteredPetType = string.Empty;

    public string EnteredPetType
    {
        get => enteredPetType;
        set
        {
            if (SetProperty(ref enteredPetType, value))
            {
                OnPropertyChanged(nameof(CanSavePet));
                ValidatePetType();
            }
        }
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
                OnPropertyChanged(nameof(CanSavePet));
                ValidatePetAge();
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
                OnPropertyChanged(nameof(CanSavePet));
                OnPropertyChanged(nameof(SelectedTypeEmoji));
                ValidatePetType();
            }
        }
    }

    // Validation error messages
    private string petNameError = string.Empty;
    public string PetNameError
    {
        get => petNameError;
        set => SetProperty(ref petNameError, value);
    }

    private string petTypeError = string.Empty;
    public string PetTypeError
    {
        get => petTypeError;
        set => SetProperty(ref petTypeError, value);
    }

    private string petAgeError = string.Empty;
    public string PetAgeError
    {
        get => petAgeError;
        set => SetProperty(ref petAgeError, value);
    }

    // Validation logic
    private bool IsPetNameValid => !string.IsNullOrWhiteSpace(EnteredPetName);

    private bool IsPetTypeValid =>
        !string.IsNullOrWhiteSpace(EnteredPetType) &&
        (SelectedPetType?.Name != "Other" || !string.IsNullOrWhiteSpace(EnteredPetType));

    private bool IsPetAgeValid => ParsedPetAge.HasValue && ParsedPetAge.Value >= 0;

    public bool CanSavePet => IsPetNameValid && IsPetTypeValid && IsPetAgeValid;

    public ObservableCollection<PetTypeOption> PetTypeOptions { get; set; } = new();

    private bool showCustomPetType;
    public bool ShowCustomPetType
    {
        get => showCustomPetType;
        set => SetProperty(ref showCustomPetType, value);
    }

    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly SettingsService _SettingsService;

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

    public PetViewModel(PetService petService, ActivePetService activePetService, SettingsService settingsService)
    {
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;

        SelectPetCommand = new Command<Pet>(SelectPet);
        AddPetCommand = new Command(async () => await SavePetAsync());
        OpenActivePetProfileCommand = new Command(() => Console.WriteLine("Open active pet profile"));
        EditActivePetCommand = new Command(() => Console.WriteLine($"Edit pet {ActivePet.Name}"));
        OpenDocumentsCommand = new Command(() => Console.WriteLine("Open documents"));
        ExportReportCommand = new Command(() => Console.WriteLine("Export report"));
        OpenMedicationDetailCommand = new Command(() => Console.WriteLine("Open medication detail"));
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
        // Validate before saving
        ValidatePetName();
        ValidatePetType();
        ValidatePetAge();

        if (!CanSavePet)
            return;

        var pet = new Pet
        {
            Name = EnteredPetName.Trim(),
            Type = EnteredPetType.Trim(),
            Age = ParsedPetAge!.Value
        };

        await _petService.SavePetAsync(pet);
        Pets.Add(pet);
        SelectPet(pet);
    }
    private Pet selectedPet = null!;
    public Pet SelectedPet
    {
        get => selectedPet;
        set
        {
            if (SetProperty(ref selectedPet, value))
            {
                SelectPet(value); // updates ActivePetService + visuals
            }
        }
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
            SelectedPet = petToSelect;
        }
    }

    private void SelectPet(Pet pet)
    {
        if (pet == null)
            return;

        foreach (var p in Pets)
            p.IsSelected = false;

        pet.IsSelected = true;

        _activePetService.ActivePet = pet;

        OnPropertyChanged(nameof(ActivePet));
        OnPropertyChanged(nameof(ActivePetEmoji));
        OnPropertyChanged(nameof(ActivePetSubtitle));
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

    public async Task<bool> IsFirstLaunchAsync()
    {
        return await _SettingsService.GetIsFirstLaunchAsync();
    }
    public async Task SetFirstLaunchFalseAsync()
    {
        await _SettingsService.SetIsFirstLaunchAsync(false);
    }
    private bool showPetCreationBackButton;
    public bool ShowPetCreationBackButton
    {
        get => showPetCreationBackButton;
        set => SetProperty(ref showPetCreationBackButton, value);
    }
    private string saveButtonLabel = string.Empty;
    public string SaveButtonLabel
    {
        get => saveButtonLabel;
        set => SetProperty(ref saveButtonLabel, value);
    }

    private string pageTitle = string.Empty;
    public string PageTitle
    {
        get => pageTitle;
        set => SetProperty(ref pageTitle, value);
    }

    private bool showBackButton;
    public bool ShowBackButton
    {
        get => showBackButton;
        set => SetProperty(ref showBackButton, value);
    }

    private bool isFirstLaunch;
    public bool IsFirstLaunch
    {
        get => isFirstLaunch;
        set => SetProperty(ref isFirstLaunch, value);
    }

    public string SelectedTypeEmoji =>
        SelectedPetType != null ? SelectedPetType.Emoji : "🐾";

    public string PreviewName =>
        !string.IsNullOrWhiteSpace(EnteredPetName) ? EnteredPetName : "Your pet";
    public async Task CheckAndSetFirstLaunchAsync()
    {
        bool isFirstLaunchValue = await IsFirstLaunchAsync();
        IsFirstLaunch = isFirstLaunchValue;

        if (isFirstLaunchValue)
        {
            SaveButtonLabel = "Let's go! 🐾";
            PageTitle = "Meet your first pet 🐾";
            ShowBackButton = false;
            await SetFirstLaunchFalseAsync();
        }
        else
        {
            SaveButtonLabel = "Add Pet";
            PageTitle = "Add a Pet";
            ShowBackButton = true;
        }
    }

    private void ValidatePetName()
    {
        PetNameError = string.IsNullOrWhiteSpace(EnteredPetName)
            ? "Pet name is required"
            : string.Empty;
    }

    private void ValidatePetType()
    {
        if (string.IsNullOrWhiteSpace(EnteredPetType))
        {
            PetTypeError = "Pet type is required";
        }
        else if (SelectedPetType?.Name == "Other" && string.IsNullOrWhiteSpace(EnteredPetType.Trim()))
        {
            PetTypeError = "Please describe your pet's type";
        }
        else
        {
            PetTypeError = string.Empty;
        }
    }

    private void ValidatePetAge()
    {
        if (string.IsNullOrWhiteSpace(EnteredPetAge))
        {
            PetAgeError = "Pet age is required";
        }
        else if (!int.TryParse(EnteredPetAge, out var age) || age < 0)
        {
            PetAgeError = "Pet age must be a valid positive number";
        }
        else
        {
            PetAgeError = string.Empty;
        }
    }


}