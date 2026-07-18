namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;

public class PetViewModel : BaseViewModel, IResettableDraft
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
    private readonly IAnalyticsService _analytics;

    public ObservableCollection<Pet> Pets { get; set; } = new ObservableCollection<Pet>();

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        private set => _activePetService.ActivePet = value;
    }

    public string ActivePetEmoji => GetEmojiForType(ActivePet.Type);

    public string ActivePetSubtitle => ActivePet == null
        ? string.Empty
        : LocalizationManager.Instance.Format("Pet_SubtitleFormat", PetTypeNames.Localize(ActivePet.Type), ActivePet.Age);

    /// <summary>Localized "Medications for {pet}" header shown on the Medications page.</summary>
    public string MedicationsHeader =>
        LocalizationManager.Instance.Format("Med_ForPet", SelectedPet?.Name ?? string.Empty);

    public ICommand SelectPetCommand { get; }
    public ICommand AddPetCommand { get; }
    public ICommand OpenMedicationDetailCommand { get; }

    // Export + Documents left this VM: the Export row opens ExportSheetViewModel's
    // sheet, and the Documents row navigates from PetsPage code-behind (pages own
    // navigation), so neither needs a command here anymore.

    public PetViewModel(PetService petService, ActivePetService activePetService, SettingsService settingsService, IAnalyticsService analytics)
    {
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;
        _analytics = analytics;

        SelectPetCommand = new Command<Pet>(SelectPet);
        AddPetCommand = new Command(async () => await SavePetAsync());
        // Stub: bound in XAML but the flow doesn't exist yet (med detail).
        OpenMedicationDetailCommand = new Command(() => System.Diagnostics.Debug.WriteLine("Open medication detail (stub)"));
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

        // Product signal: "do users create pets?" and the rough species mix. We send a
        // COARSE species bucket only — a known type lowercased, or "other" for any
        // custom free-text — never the raw type string, which a user could make
        // identifying.
        _analytics.Track(AnalyticsEvents.PetCreated, new Dictionary<string, object?>
        {
            [AnalyticsEvents.PropSpecies] = NormalizeSpecies(pet.Type),
        });

        // First launch completes when the first pet is actually SAVED — flipping
        // it on page-view meant killing the app on the form gave a returning-user
        // experience with no pet.
        if (IsFirstLaunch)
        {
            await SetFirstLaunchFalseAsync();
            IsFirstLaunch = false;
        }

        // Leave the form in a clean state so the next "add pet" starts fresh.
        ResetDraft();
    }

    /// <summary>
    /// Prefill the create/edit form from the active pet — the edit-pet door from the
    /// Manage page. Matches the stored type to a known option (else "Other" with the
    /// custom text), and clears any residual validation errors.
    /// </summary>
    public void LoadDraftFromActivePet()
    {
        var pet = ActivePet;
        if (pet == null)
            return;

        EnteredPetName = pet.Name;
        EnteredPetAge = pet.Age.ToString();

        var match = PetTypeOptions.FirstOrDefault(
            o => string.Equals(o.Name, pet.Type, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectedPetType = match; // setter mirrors the name into EnteredPetType
        }
        else
        {
            SelectedPetType = PetTypeOptions.FirstOrDefault(o => o.Name == "Other");
            EnteredPetType = pet.Type; // a custom type: keep the stored text
        }

        PetNameError = string.Empty;
        PetTypeError = string.Empty;
        PetAgeError = string.Empty;
    }

    /// <summary>Set the form's title + button for edit mode ("You're editing {X}").</summary>
    public void ConfigureForEdit()
    {
        PageTitle = LocalizationManager.Instance.Format("CreatePet_EditTitle", ActivePet?.Name ?? string.Empty);
        SaveButtonLabel = LocalizationManager.Instance.GetString("CreatePet_EditSave");
        ShowBackButton = true;
    }

    /// <summary>Save an edit in place: update the active pet's fields and persist. No
    /// new pet, no condition picker — the Manage page just pops back.</summary>
    public async Task<bool> SaveEditedPetAsync()
    {
        ValidatePetName();
        ValidatePetType();
        ValidatePetAge();

        if (!CanSavePet)
            return false;

        var pet = ActivePet;
        if (pet == null)
            return false;

        pet.Name = EnteredPetName.Trim();
        pet.Type = EnteredPetType.Trim();
        pet.Age = ParsedPetAge!.Value;

        await _petService.UpdatePetAsync(pet);

        // Refresh anything bound to the active pet (Care card, subtitle, emoji).
        OnPropertyChanged(nameof(ActivePet));
        OnPropertyChanged(nameof(ActivePetEmoji));
        OnPropertyChanged(nameof(ActivePetSubtitle));
        return true;
    }

    /// <summary>
    /// Clears the pet-creation form back to a blank draft. The entry setters
    /// re-run validation as they are cleared, so the error strings are wiped
    /// last to leave a fresh form with no residual "required" messages.
    /// </summary>
    public void ResetDraft()
    {
        SelectedPetType = null;
        EnteredPetName = string.Empty;
        EnteredPetType = string.Empty;
        EnteredPetAge = string.Empty;
        ShowCustomPetType = false;

        PetNameError = string.Empty;
        PetTypeError = string.Empty;
        PetAgeError = string.Empty;
    }
    private Pet selectedPet = null!;
    public Pet SelectedPet
    {
        get => selectedPet;
        set
        {
            if (SetProperty(ref selectedPet, value))
            {
                OnPropertyChanged(nameof(MedicationsHeader));
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

    /// <summary>Map a pet's stored type to a coarse, non-identifying species bucket for
    /// analytics. Only the fixed known types pass through; any custom/free-text type
    /// collapses to "other" so nothing a user typed is ever transmitted.</summary>
    private static string NormalizeSpecies(string? type) =>
        type?.Trim().ToLowerInvariant() switch
        {
            "dog" => "dog",
            "cat" => "cat",
            "bird" => "bird",
            "rabbit" => "rabbit",
            "fish" => "fish",
            _ => AnalyticsEvents.SpeciesOther,
        };

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
        !string.IsNullOrWhiteSpace(EnteredPetName) ? EnteredPetName : LocalizationManager.Instance.GetString("CreatePet_PreviewName");
    public async Task CheckAndSetFirstLaunchAsync()
    {
        bool isFirstLaunchValue = await IsFirstLaunchAsync();
        IsFirstLaunch = isFirstLaunchValue;

        if (isFirstLaunchValue)
        {
            SaveButtonLabel = LocalizationManager.Instance.GetString("CreatePet_FirstLaunchSave");
            PageTitle = LocalizationManager.Instance.GetString("CreatePet_FirstLaunchTitle");
            ShowBackButton = false;
            // The flag is cleared in SavePetAsync, once the first pet is saved.
        }
        else
        {
            SaveButtonLabel = LocalizationManager.Instance.GetString("CreatePet_AddSave");
            PageTitle = LocalizationManager.Instance.GetString("CreatePet_AddTitle");
            ShowBackButton = true;
        }
    }

    private void ValidatePetName()
    {
        PetNameError = string.IsNullOrWhiteSpace(EnteredPetName)
            ? LocalizationManager.Instance.GetString("Validation_PetNameRequired")
            : string.Empty;
    }

    private void ValidatePetType()
    {
        if (string.IsNullOrWhiteSpace(EnteredPetType))
        {
            PetTypeError = LocalizationManager.Instance.GetString("Validation_PetTypeRequired");
        }
        else if (SelectedPetType?.Name == "Other" && string.IsNullOrWhiteSpace(EnteredPetType.Trim()))
        {
            PetTypeError = LocalizationManager.Instance.GetString("Validation_PetTypeDescribe");
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
            PetAgeError = LocalizationManager.Instance.GetString("Validation_PetAgeRequired");
        }
        else if (!int.TryParse(EnteredPetAge, out var age) || age < 0)
        {
            PetAgeError = LocalizationManager.Instance.GetString("Validation_PetAgeInvalid");
        }
        else
        {
            PetAgeError = string.Empty;
        }
    }


}