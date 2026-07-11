namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>One selectable condition row in the picker.</summary>
public class ConditionOption : BaseViewModel
{
    public ConditionOption(Condition condition) => Condition = condition;

    public Condition Condition { get; }
    public string Name => Condition.Name;
    public string Icon => Condition.Icon;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Backs the condition-picker screen shown right after a pet is created. The
/// user chooses one condition (or "None / Not sure"); the choice is written to
/// <see cref="Pet.ConditionId"/> on the active pet.
/// </summary>
public class ConditionPickerViewModel : BaseViewModel
{
    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;

    public ObservableCollection<ConditionOption> Conditions { get; } = new();
    public ICommand SelectCommand { get; }

    public ConditionPickerViewModel(PetService petService, ActivePetService activePetService)
    {
        _petService = petService;
        _activePetService = activePetService;

        SelectCommand = new Command<ConditionOption>(Select);

        foreach (var condition in ConditionCatalog.Conditions)
            Conditions.Add(new ConditionOption(condition));
    }

    private ConditionOption? _selected;
    public ConditionOption? Selected
    {
        get => _selected;
        private set
        {
            if (SetProperty(ref _selected, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => Selected != null;

    public string PetName => _activePetService.ActivePet?.Name ?? string.Empty;
    public string TitleText => $"Does {PetName} have an ongoing condition?";
    public string SubtitleText =>
        $"Pick one so Felova can help you track the right things for {PetName}. You can change this later.";

    /// <summary>Re-sync the highlighted row to whatever is stored on the active pet
    /// (empty → "None"). Call from the page's OnAppearing.</summary>
    public void Sync()
    {
        var id = _activePetService.ActivePet?.ConditionId ?? string.Empty;
        var match = Conditions.FirstOrDefault(o => o.Condition.Id == id) ?? Conditions.FirstOrDefault();
        Select(match);

        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
    }

    private void Select(ConditionOption? option)
    {
        if (option == null)
            return;

        foreach (var o in Conditions)
            o.IsSelected = ReferenceEquals(o, option);

        Selected = option;
    }

    /// <summary>Persist the chosen condition onto the active pet.</summary>
    public async Task SaveAsync()
    {
        var pet = _activePetService.ActivePet;
        if (pet == null || Selected == null)
            return;

        pet.ConditionId = Selected.Condition.Id;
        await _petService.UpdatePetAsync(pet);
    }
}
