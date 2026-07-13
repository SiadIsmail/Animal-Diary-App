namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>One selectable condition row in the picker (multi-select).</summary>
public class ConditionOption : BaseViewModel
{
    public ConditionOption(Condition condition) => Condition = condition;

    public Condition Condition { get; }
    public string Id => Condition.Id;
    public string Name => Condition.Name;
    public string Icon => Condition.Icon;

    /// <summary>The gentle "None / Not sure" sentinel (empty id) — mutually exclusive
    /// with the real conditions and never removable.</summary>
    public bool IsNone => string.IsNullOrEmpty(Condition.Id);

    /// <summary>True when picking this condition opens a setup sheet to configure it.</summary>
    public bool HasSheet => ConditionSetup.HasSheet(Condition.Id);

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(ShowRemove));
        }
    }

    /// <summary>A ✕ (toggle off) shows only on selected REAL conditions; None never.</summary>
    public bool ShowRemove => IsSelected && !IsNone;
}

/// <summary>
/// Backs the condition picker shown right after a pet is created (onboarding, or
/// "add pet"). Interactive multi-select — NOT a wizard: picking a configurable
/// condition opens the SAME reusable setup sheet the Manage page uses (the second
/// "door"), and the row shows a check once it's set up; a condition with no options
/// just checks immediately. Everything persists to the active pet as you go (via the
/// multi-condition store), so Continue simply hands off to the app.
/// </summary>
public class ConditionPickerViewModel : BaseViewModel
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private readonly PetService _petService;
    private readonly ActivePetService _activePetService;
    private readonly PetConditionService _conditions;
    private readonly TrackerService _trackers;

    public ObservableCollection<ConditionOption> Conditions { get; } = new();
    public ICommand SelectCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Ask the page to open the reusable setup sheet for this condition id.</summary>
    public event Action<string>? RequestConditionSetup;

    public ConditionPickerViewModel(
        PetService petService,
        ActivePetService activePetService,
        PetConditionService conditions,
        TrackerService trackers)
    {
        _petService = petService;
        _activePetService = activePetService;
        _conditions = conditions;
        _trackers = trackers;

        SelectCommand = new Command<ConditionOption>(async o => await OnSelectAsync(o));
        RemoveCommand = new Command<ConditionOption>(async o => await OnRemoveAsync(o));

        foreach (var condition in ConditionCatalog.Conditions)
            Conditions.Add(new ConditionOption(condition));
    }

    public string PetName => _activePetService.ActivePet?.Name ?? string.Empty;
    public string TitleText => Loc.Format("ConditionPicker_Title", PetName);
    public string SubtitleText => Loc.Format("ConditionPicker_Sub", PetName);

    /// <summary>Recompute every row's selected state from the pet's stored conditions.
    /// Call from the page's OnAppearing and after any setup sheet saves.</summary>
    public async Task SyncAsync()
    {
        var ids = (await _conditions.GetConditionIdsAsync(_activePetService.ActivePet)).ToHashSet();

        foreach (var o in Conditions)
            o.IsSelected = o.IsNone ? ids.Count == 0 : ids.Contains(o.Id);

        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
    }

    private async Task OnSelectAsync(ConditionOption? option)
    {
        var pet = _activePetService.ActivePet;
        if (option == null || pet == null || pet.Id == 0)
            return;

        if (option.IsNone)
        {
            // "None / Not sure" clears every real condition (and its trackers).
            await ClearAllConditionsAsync(pet.Id);
            await SyncAsync();
            return;
        }

        if (option.HasSheet)
        {
            // Open the reusable setup sheet (to configure, or to edit if already set
            // up). It persists + the page re-syncs when it saves.
            RequestConditionSetup?.Invoke(option.Id);
            return;
        }

        // A condition with no options: toggle it on/off immediately.
        if (option.IsSelected)
            await RemoveConditionAsync(pet.Id, option.Id);
        else
            await AddConditionDirectAsync(pet.Id, option.Id);

        await SyncAsync();
    }

    private async Task OnRemoveAsync(ConditionOption? option)
    {
        var pet = _activePetService.ActivePet;
        if (option == null || option.IsNone || pet == null || pet.Id == 0)
            return;

        await RemoveConditionAsync(pet.Id, option.Id);
        await SyncAsync();
    }

    // ── Persistence helpers (mirror the Manage page's add/remove) ────────────────
    private async Task AddConditionDirectAsync(int petId, string conditionId)
    {
        await _conditions.AddAsync(petId, conditionId);
        await _trackers.EnsureSeededAsync(petId, System.Array.Empty<string>());
        foreach (var seed in CarePlanCatalog.ForCondition(conditionId))
            await _trackers.UpsertAsync(petId, seed.TrackerId, t =>
            {
                t.Kind = seed.Kind;
                t.PerDayCount = seed.PerDayCount;
                t.Unit = seed.Unit;
                t.FromCondition ??= conditionId;
            });
    }

    private async Task RemoveConditionAsync(int petId, string conditionId)
    {
        await _conditions.RemoveAsync(petId, conditionId);
        var linked = (await _trackers.GetForPetAsync(petId))
            .Where(t => t.FromCondition == conditionId)
            .ToList();
        foreach (var t in linked)
            await _trackers.DeleteAsync(t);
    }

    private async Task ClearAllConditionsAsync(int petId)
    {
        var ids = await _conditions.GetConditionIdsAsync(_activePetService.ActivePet);
        foreach (var id in ids.ToList())
            await RemoveConditionAsync(petId, id);
    }

    /// <summary>Hand-off bookkeeping on Continue: keep the legacy single
    /// <see cref="Pet.ConditionId"/> pointed at the primary condition (for the older
    /// calendar path); the multi-condition store is authoritative.</summary>
    public async Task SaveAsync()
    {
        var pet = _activePetService.ActivePet;
        if (pet == null)
            return;

        var ids = (await _conditions.GetConditionIdsAsync(pet)).ToList();
        pet.ConditionId = ids.FirstOrDefault() ?? string.Empty;
        await _petService.UpdatePetAsync(pet);
    }
}
