namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>One chip on the Care page's active-pet card: a condition the pet has, or
/// its medication count. The label is resolved per read (the
/// <see cref="DaySelectionItem"/> pattern) so a live language switch re-translates
/// the chips — the owning VM is a singleton, so a name cached at construction would
/// stay in the old language.</summary>
public class PetProfileTag : BaseViewModel
{
    /// <summary>AppStrings key: a condition name key, or the medication count format.</summary>
    public string ResourceKey { get; init; } = string.Empty;

    /// <summary>Format argument for the count chips; null for a plain condition name.</summary>
    public int? Count { get; init; }

    /// <summary>Medication chips take the teal accent; conditions stay neutral.</summary>
    public bool IsMedication { get; init; }

    public string Label => Count is null
        ? LocalizationManager.Instance.GetString(ResourceKey)
        : LocalizationManager.Instance.Format(ResourceKey, Count.Value);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

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
                OnPropertyChanged(nameof(CanContinueIdentity));
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

    // ── Birthday ─────────────────────────────────────────────────────────────────
    // The pet's birthday, entered as three independent parts. Only the year is
    // required; owners who only know roughly when their pet was born leave month
    // and/or day blank. We never fill an unknown part with a placeholder value.

    private string enteredBirthYear = string.Empty;
    public string EnteredBirthYear
    {
        get => enteredBirthYear;
        set
        {
            if (SetProperty(ref enteredBirthYear, value))
            {
                OnPropertyChanged(nameof(ParsedBirthYear));
                OnPropertyChanged(nameof(CanSavePet));
                ValidateBirthday();
            }
        }
    }

    private string enteredBirthMonth = string.Empty;
    public string EnteredBirthMonth
    {
        get => enteredBirthMonth;
        set
        {
            if (SetProperty(ref enteredBirthMonth, value))
            {
                OnPropertyChanged(nameof(ParsedBirthMonth));
                OnPropertyChanged(nameof(CanSavePet));
                ValidateBirthday();
            }
        }
    }

    private string enteredBirthDay = string.Empty;
    public string EnteredBirthDay
    {
        get => enteredBirthDay;
        set
        {
            if (SetProperty(ref enteredBirthDay, value))
            {
                OnPropertyChanged(nameof(ParsedBirthDay));
                OnPropertyChanged(nameof(CanSavePet));
                ValidateBirthday();
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

    private string birthdayError = string.Empty;
    public string BirthdayError
    {
        get => birthdayError;
        set => SetProperty(ref birthdayError, value);
    }

    // Validation logic
    private bool IsPetNameValid => !string.IsNullOrWhiteSpace(EnteredPetName);

    private bool IsPetTypeValid =>
        !string.IsNullOrWhiteSpace(EnteredPetType) &&
        (SelectedPetType?.Name != "Other" || !string.IsNullOrWhiteSpace(EnteredPetType));

    /// <summary>The birthday is valid when a plausible year is present and every part
    /// the owner DID fill in is itself valid and not in the future. Blank month/day are
    /// fine — they're optional. <see cref="ComputeBirthdayError"/> is the single source
    /// of truth; this just asks whether it found nothing wrong.</summary>
    private bool IsBirthdayValid => ComputeBirthdayError() is null;

    /// <summary>Gate for the Identity page (page 1): only the name is entered there.</summary>
    public bool CanContinueIdentity => IsPetNameValid;

    /// <summary>Gate for the final save (page 2): everything must be valid.</summary>
    public bool CanSavePet => IsPetNameValid && IsPetTypeValid && IsBirthdayValid;

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
    private readonly PetConditionService _conditions;
    private readonly MedicationService _medications;

    public ObservableCollection<Pet> Pets { get; set; } = new ObservableCollection<Pet>();

    public Pet ActivePet
    {
        get => _activePetService.ActivePet;
        private set => _activePetService.ActivePet = value;
    }

    public string ActivePetEmoji => GetEmojiForType(ActivePet.Type);

    /// <summary>Chips under the active pet's name: one per condition it actually has,
    /// then its medication count. Read-only display of what the condition and
    /// medication stores already hold — the Care card states, it doesn't edit
    /// (Manage owns that).</summary>
    public ObservableCollection<PetProfileTag> ActivePetTags { get; } = new();

    /// <summary>Rebuild <see cref="ActivePetTags"/> from the authoritative stores.
    /// Conditions come from <see cref="PetConditionService"/> (never the legacy
    /// <c>Pet.ConditionId</c>); the count covers non-archived medications only, the
    /// same filter the Manage page applies. Chips with nothing to say are omitted
    /// rather than rendered as an empty state.</summary>
    public async Task LoadActivePetTagsAsync()
    {
        var pet = ActivePet;

        ActivePetTags.Clear();
        if (pet == null || pet.Id == 0)
            return;

        // Gather before touching the observable collection.
        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        var medCount = (await _medications.GetMedicationsByPetIdAsync(pet.Id))
            .Count(m => !m.IsArchived);

        foreach (var id in conditionIds)
            ActivePetTags.Add(new PetProfileTag { ResourceKey = ConditionCatalog.GetCondition(id).NameKey });

        if (medCount > 0)
            ActivePetTags.Add(new PetProfileTag
            {
                ResourceKey = medCount == 1 ? "Pets_MedicationCountOne" : "Pets_MedicationCountMany",
                Count = medCount,
                IsMedication = true
            });
    }

    // Type · age, but the age half is dropped when the pet's age is unknown so we never
    // render a bare "· yrs". In practice a birthday's year is always known, so the age
    // form is the norm; the fallback only covers legacy pets with no stored age.
    public string ActivePetSubtitle
    {
        get
        {
            if (ActivePet == null)
                return string.Empty;

            var type = PetTypeNames.Localize(ActivePet.Type);
            return ActivePet.AgeYears is int years
                ? LocalizationManager.Instance.Format("Pet_SubtitleFormat", type, years)
                : type;
        }
    }

    /// <summary>Localized "Medications for {pet}" header shown on the Medications page.</summary>
    public string MedicationsHeader =>
        LocalizationManager.Instance.Format("Med_ForPet", SelectedPet?.Name ?? string.Empty);

    public ICommand SelectPetCommand { get; }
    public ICommand AddPetCommand { get; }
    public ICommand OpenMedicationDetailCommand { get; }

    // Export + Documents left this VM: the Export row opens ExportSheetViewModel's
    // sheet, and the Documents row navigates from PetsPage code-behind (pages own
    // navigation), so neither needs a command here anymore.

    public PetViewModel(PetService petService, ActivePetService activePetService, SettingsService settingsService, IAnalyticsService analytics,
        PetConditionService conditions, MedicationService medications)
    {
        _petService = petService;
        _activePetService = activePetService;
        _SettingsService = settingsService;
        _analytics = analytics;
        _conditions = conditions;
        _medications = medications;

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
                // The chips describe the active pet — they have to follow a switch.
                LoadActivePetTagsAsync().Forget();
            }
        };

        // Chip labels are resolved per read; nudge the bindings after a live switch.
        LocalizationManager.Instance.PropertyChanged += (s, e) =>
        {
            foreach (var tag in ActivePetTags)
                tag.RefreshLabel();
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
        ValidateBirthday();

        if (!CanSavePet)
            return;

        var pet = new Pet
        {
            Name = EnteredPetName.Trim(),
            Type = EnteredPetType.Trim(),
            BirthYear = ParsedBirthYear!.Value,
            BirthMonth = ParsedBirthMonth,
            BirthDay = ParsedBirthDay,
        };
        // Snapshot the derived age into the legacy column so anything still reading it
        // stays roughly right; AgeYears remains the live, birthday-derived source.
        pet.Age = pet.AgeYears ?? 0;

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

        // Prefill the birthday from whatever the pet stored. A pet created before the
        // birthday system has no BirthYear but does have a legacy age — offer a
        // best-guess year (today − age) as a starting point the owner can correct;
        // it is only persisted if they save.
        if (pet.BirthYear > 0)
            EnteredBirthYear = pet.BirthYear.ToString();
        else if (pet.Age > 0)
            EnteredBirthYear = (DateTime.Today.Year - pet.Age).ToString();
        else
            EnteredBirthYear = string.Empty;

        EnteredBirthMonth = pet.BirthMonth?.ToString() ?? string.Empty;
        EnteredBirthDay = pet.BirthDay?.ToString() ?? string.Empty;

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
        BirthdayError = string.Empty;
    }

    /// <summary>Set both pages' titles + the final button for edit mode
    /// ("You're editing {X}"). Both onboarding pages share one edit heading.</summary>
    public void ConfigureForEdit()
    {
        var editTitle = LocalizationManager.Instance.Format("CreatePet_EditTitle", ActivePet?.Name ?? string.Empty);
        IdentityTitle = editTitle;
        DetailsTitle = editTitle;
        IdentitySubtitle = string.Empty;
        DetailsSubtitle = string.Empty;
        SaveButtonLabel = LocalizationManager.Instance.GetString("CreatePet_EditSave");
        ShowBackButton = true;
    }

    /// <summary>Save an edit in place: update the active pet's fields and persist. No
    /// new pet, no condition picker — the Manage page just pops back.</summary>
    public async Task<bool> SaveEditedPetAsync()
    {
        ValidatePetName();
        ValidatePetType();
        ValidateBirthday();

        if (!CanSavePet)
            return false;

        var pet = ActivePet;
        if (pet == null)
            return false;

        pet.Name = EnteredPetName.Trim();
        pet.Type = EnteredPetType.Trim();
        pet.BirthYear = ParsedBirthYear!.Value;
        pet.BirthMonth = ParsedBirthMonth;
        pet.BirthDay = ParsedBirthDay;
        pet.Age = pet.AgeYears ?? 0; // keep the legacy snapshot in step

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
        EnteredBirthYear = string.Empty;
        EnteredBirthMonth = string.Empty;
        EnteredBirthDay = string.Empty;
        ShowCustomPetType = false;

        PetNameError = string.Empty;
        PetTypeError = string.Empty;
        BirthdayError = string.Empty;
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

    // Parsed birthday parts: null when the field is blank OR unparseable. Blank month
    // and day are legitimate ("unknown"); the year is the only required part, enforced
    // by validation rather than here.
    public int? ParsedBirthYear =>
        int.TryParse(EnteredBirthYear, out var y) ? y : null;
    public int? ParsedBirthMonth =>
        int.TryParse(EnteredBirthMonth, out var m) ? m : null;
    public int? ParsedBirthDay =>
        int.TryParse(EnteredBirthDay, out var d) ? d : null;

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
    /// <summary>Label on the final (Details page) save/create button.</summary>
    public string SaveButtonLabel
    {
        get => saveButtonLabel;
        set => SetProperty(ref saveButtonLabel, value);
    }

    // The onboarding form is split across two pages (Identity → Details); each has its
    // own heading and optional subtitle, set together in the configure methods below.
    private string identityTitle = string.Empty;
    public string IdentityTitle
    {
        get => identityTitle;
        set => SetProperty(ref identityTitle, value);
    }

    private string identitySubtitle = string.Empty;
    public string IdentitySubtitle
    {
        get => identitySubtitle;
        set => SetProperty(ref identitySubtitle, value);
    }

    private string detailsTitle = string.Empty;
    public string DetailsTitle
    {
        get => detailsTitle;
        set => SetProperty(ref detailsTitle, value);
    }

    private string detailsSubtitle = string.Empty;
    public string DetailsSubtitle
    {
        get => detailsSubtitle;
        set => SetProperty(ref detailsSubtitle, value);
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

        var loc = LocalizationManager.Instance;
        if (isFirstLaunchValue)
        {
            IdentityTitle = loc.GetString("CreatePet_Identity_FirstLaunchTitle");
            IdentitySubtitle = loc.GetString("CreatePet_Identity_Subtitle");
            DetailsTitle = loc.GetString("CreatePet_Details_FirstLaunchTitle");
            DetailsSubtitle = loc.GetString("CreatePet_Details_Subtitle");
            SaveButtonLabel = loc.GetString("CreatePet_FirstLaunchSave");
            ShowBackButton = false;
            // The flag is cleared in SavePetAsync, once the first pet is saved.
        }
        else
        {
            IdentityTitle = loc.GetString("CreatePet_Identity_AddTitle");
            IdentitySubtitle = loc.GetString("CreatePet_Identity_Subtitle");
            DetailsTitle = loc.GetString("CreatePet_Details_AddTitle");
            DetailsSubtitle = loc.GetString("CreatePet_Details_Subtitle");
            SaveButtonLabel = loc.GetString("CreatePet_AddSave");
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

    private void ValidateBirthday()
    {
        var key = ComputeBirthdayError();
        BirthdayError = key is null ? string.Empty : LocalizationManager.Instance.GetString(key);
    }

    /// <summary>The single rule set for the birthday. Returns the resource key of the
    /// first problem found, or null when the birthday is acceptable. Enforces: a
    /// required, plausible year; an optional month in 1–12; an optional day that needs
    /// a month, must exist in that month, and — with the whole date known — must not be
    /// in the future. A year-only or year+month birthday is always allowed.</summary>
    private string? ComputeBirthdayError()
    {
        // Year — required.
        if (string.IsNullOrWhiteSpace(EnteredBirthYear))
            return "Validation_BirthYearRequired";

        var thisYear = DateTime.Today.Year;
        // Lower bound is generous (no pet outlives it) but rules out typos like "202".
        if (ParsedBirthYear is not int year || year < 1900 || year > thisYear)
            return "Validation_BirthYearInvalid";

        // Month — optional, but if given must be a real month.
        int? month = null;
        if (!string.IsNullOrWhiteSpace(EnteredBirthMonth))
        {
            if (ParsedBirthMonth is not int m || m < 1 || m > 12)
                return "Validation_BirthMonthInvalid";
            month = m;
        }

        // Day — optional; only meaningful with a month, and must exist in it.
        if (!string.IsNullOrWhiteSpace(EnteredBirthDay))
        {
            if (month is null)
                return "Validation_BirthDayNeedsMonth";
            if (ParsedBirthDay is not int day || day < 1 || day > DateTime.DaysInMonth(year, month.Value))
                return "Validation_BirthDayInvalid";

            // A fully known birthday can't be in the future.
            if (new DateTime(year, month.Value, day) > DateTime.Today)
                return "Validation_BirthdayFuture";
        }

        return null;
    }
}