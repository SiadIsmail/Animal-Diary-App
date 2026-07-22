namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

// ── Small display records for the page's bindable lists ──────────────────────────

/// <summary>Common type for anything the Conditions section's FlexLayout can render —
/// a condition chip, the trailing Add chip, and (later) things like a loading or
/// recommendation chip. Add a new item type + template + selector case to extend.</summary>
public interface IConditionChipItem
{
}

/// <summary>A condition chip on the Manage page (name + emoji + its id for removal).</summary>
public class ManageConditionChip : IConditionChipItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}

/// <summary>Sentinel item for the trailing "Add Condition" chip in the Conditions FlexLayout.</summary>
public class AddConditionChipItem : IConditionChipItem
{
    public static readonly AddConditionChipItem Instance = new();
}

/// <summary>One care-plan row: a tracker with its cadence description and the
/// breadcrumb of the condition that introduced it.</summary>
public class CarePlanRow
{
    public TrackerId TrackerId { get; init; }
    public string? FromCondition { get; init; }
    public string Icon { get; init; } = string.Empty;
    public Color IconBackground { get; init; } = Colors.Transparent;
    public Color IconForeground { get; init; } = Colors.Black;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FromLabel { get; init; } = string.Empty;
    public bool HasFrom => !string.IsNullOrEmpty(FromLabel);
}

/// <summary>A chip in the "so {pet}'s Journal will ask for…" preview strip.</summary>
public class PreviewChip
{
    public string Icon { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}

/// <summary>A medication row in the Manage page's Medications section.</summary>
public class ManageMedRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>A condition offered in the "add condition" sheet.</summary>
public class AddConditionOption
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
}

/// <summary>What the page's native remove dialogs decided: cancel, "save a copy
/// first" (the page opened the export sheet, nothing to delete), or go ahead.</summary>
public enum PetRemovalFlowResult
{
    Cancel,
    SavedCopy,
    Proceed,
}

/// <summary>A cadence option in the generic "adjust tracker" sheet (Mood / Weight).</summary>
public class AdjustOption : BaseViewModel
{
    public TrackerKind Kind { get; init; }
    public string Label { get; init; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Backs the Manage Pet page (the felova-pet-page prototype). Reads the active pet's
/// identity, its conditions (<see cref="PetConditionService"/>) and its persisted
/// care plan (<see cref="CarePlanService"/>), and coordinates the page's own sheets
/// (add condition, remove condition, adjust a default tracker). The three condition
/// SETUP sheets are separate reusable VMs on <see cref="MainViewModel"/>; this VM asks
/// the page to open them via <see cref="RequestConditionSetup"/> ("one menu, two
/// doors"). New functionality, so it's a new VM — the CalendarViewModel is untouched.
/// </summary>
public class ManagePetViewModel : BaseViewModel
{
    private static LocalizationManager Loc => LocalizationManager.Instance;
    private static readonly CultureInfo Ci = CultureInfo.CurrentCulture;

    private readonly ActivePetService _activePet;
    private readonly PetConditionService _conditions;
    private readonly TrackerService _trackers;
    private readonly CarePlanService _carePlan;
    private readonly MedicationService _medications;
    private readonly PetDeletionService _deletion;

    public ManagePetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers,
        CarePlanService carePlan,
        MedicationService medications,
        PetDeletionService deletion)
    {
        _activePet = activePet;
        _conditions = conditions;
        _trackers = trackers;
        _carePlan = carePlan;
        _medications = medications;
        _deletion = deletion;

        TapIdentityCommand = new Command(() => RequestEditPet?.Invoke());
        AddConditionCommand = new Command(OpenAddConditionSheet);
        RemoveConditionCommand = new Command<ManageConditionChip>(OpenRemoveSheet);
        TapCarePlanRowCommand = new Command<CarePlanRow>(async r => await OnTapCarePlanRowAsync(r));
        AddMedicationCommand = new Command(() => RequestAddMedication?.Invoke());
        TapMedicationCommand = new Command<ManageMedRow>(m => { if (m != null) RequestOpenMedication?.Invoke(m.Id); });

        PickAddConditionCommand = new Command<AddConditionOption>(async o => await OnPickAddConditionAsync(o));
        CloseAddConditionCommand = new Command(() => IsAddConditionSheetVisible = false);

        RemoveWithTrackersCommand = new Command(async () => await RemoveConditionAsync(alsoTrackers: true));
        RemoveKeepTrackersCommand = new Command(async () => await RemoveConditionAsync(alsoTrackers: false));
        CancelRemoveCommand = new Command(() => IsRemoveSheetVisible = false);

        SelectAdjustCommand = new Command<AdjustOption>(SelectAdjust);
        SaveAdjustCommand = new Command(async () => await SaveAdjustAsync());
        TurnOffTrackerCommand = new Command(async () => await TurnOffTrackerAsync());
        CloseAdjustCommand = new Command(() => IsAdjustSheetVisible = false);

        RemovePetCommand = new Command(async () => await OnRemovePetAsync());
    }

    // ── Events the page acts on ──────────────────────────────────────────────────
    /// <summary>Open the reusable setup sheet for this condition id (diabetes/ckd/epilepsy).</summary>
    public event Action<string>? RequestConditionSetup;
    /// <summary>Open the edit-pet door (prefilled CreatePetPage).</summary>
    public event Action? RequestEditPet;
    /// <summary>Open the medication add flow.</summary>
    public event Action? RequestAddMedication;
    /// <summary>Open a specific medication (by id).</summary>
    public event Action<int>? RequestOpenMedication;

    /// <summary>Ask the page to run the native remove dialogs for this pet (native
    /// alerts + the export sheet belong to the page, not the VM). The page offers the
    /// export first, names the consequence, and returns what to do.</summary>
    public Func<PetRemovalKind, Task<PetRemovalFlowResult>>? RequestRemoveFlow;

    /// <summary>Raised after the active pet was removed. The flag is whether any pet
    /// is left: true → pop back to the pet list; false → the owner deleted their last
    /// pet, so the page returns to onboarding.</summary>
    public event Action<bool>? PetRemoved;

    // ── Bindable lists ───────────────────────────────────────────────────────────
    public ObservableCollection<ManageConditionChip> Conditions { get; } = new();

    /// <summary>What the Conditions FlexLayout actually renders: every condition chip
    /// followed by the Add chip, rebuilt whenever <see cref="Conditions"/> changes so
    /// Add always sorts last and wraps like any other chip.</summary>
    public ObservableCollection<IConditionChipItem> ConditionItems { get; } = new();
    public ObservableCollection<CarePlanRow> CarePlanRows { get; } = new();
    public ObservableCollection<PreviewChip> PreviewChips { get; } = new();
    public ObservableCollection<ManageMedRow> Medications { get; } = new();

    // These two live inside a FelovaBottomSheet and are populated on open, so they are
    // NOT in-place-mutated collections: assigning a fresh list raises one property
    // change and the sheet's ItemsSource binding rebuilds its rows from scratch.
    private IReadOnlyList<AddConditionOption> _addConditionOptions = System.Array.Empty<AddConditionOption>();
    public IReadOnlyList<AddConditionOption> AddConditionOptions
    {
        get => _addConditionOptions;
        private set => SetProperty(ref _addConditionOptions, value);
    }

    private IReadOnlyList<AdjustOption> _adjustOptions = System.Array.Empty<AdjustOption>();
    public IReadOnlyList<AdjustOption> AdjustOptions
    {
        get => _adjustOptions;
        private set => SetProperty(ref _adjustOptions, value);
    }

    // ── Identity ─────────────────────────────────────────────────────────────────
    public string PetName => _activePet.ActivePet?.Name ?? string.Empty;
    public string PetInitial => string.IsNullOrEmpty(PetName) ? "·" : PetName.Substring(0, 1).ToUpperInvariant();
    // Age is dropped when unknown so the subtitle never reads "· yrs" with no number
    // (see PetViewModel.ActivePetSubtitle — same rule).
    public string PetSubtitle => _activePet.ActivePet is { } p
        ? (p.AgeYears is int years
            ? Loc.Format("Pet_SubtitleFormat", PetTypeNames.Localize(p.Type), years)
            : PetTypeNames.Localize(p.Type))
        : string.Empty;
    public string IdentityHint => Loc.GetString("Manage_IdentityHint");

    public string PreviewLabel => Loc.Format("Manage_PreviewLabel", PetName);

    public bool HasCarePlan => CarePlanRows.Count > 0;
    public bool HasNoCarePlan => CarePlanRows.Count == 0;
    public string EmptyPlanText => Loc.GetString("Manage_EmptyPlan");

    // ── Commands ─────────────────────────────────────────────────────────────────
    public ICommand TapIdentityCommand { get; }
    public ICommand AddConditionCommand { get; }
    public ICommand RemoveConditionCommand { get; }
    public ICommand TapCarePlanRowCommand { get; }
    public ICommand AddMedicationCommand { get; }
    public ICommand TapMedicationCommand { get; }

    public ICommand PickAddConditionCommand { get; }
    public ICommand CloseAddConditionCommand { get; }
    public ICommand RemoveWithTrackersCommand { get; }
    public ICommand RemoveKeepTrackersCommand { get; }
    public ICommand CancelRemoveCommand { get; }
    public ICommand SelectAdjustCommand { get; }
    public ICommand SaveAdjustCommand { get; }
    public ICommand TurnOffTrackerCommand { get; }
    public ICommand CloseAdjustCommand { get; }
    public ICommand RemovePetCommand { get; }

    /// <summary>Label for the destructive remove row, e.g. "Remove Charly from Felova".</summary>
    public string RemovePetLabel => Loc.Format("Manage_RemovePetRow", PetName);

    // ── Load ─────────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        // Refresh identity text.
        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(PetInitial));
        OnPropertyChanged(nameof(PetSubtitle));
        OnPropertyChanged(nameof(PreviewLabel));
        OnPropertyChanged(nameof(IdentityHint));
        OnPropertyChanged(nameof(EmptyPlanText));
        OnPropertyChanged(nameof(RemovePetLabel));

        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
        {
            ClearAll();
            return;
        }

        // Gather everything before touching the observable collections.
        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        var plan = (await _carePlan.GetPlanAsync(pet)).ToList();
        var meds = (await _medications.GetMedicationsByPetIdAsync(pet.Id))
            .Where(m => !m.IsArchived).ToList();

        // ── atomic fill ──
        Conditions.Clear();
        foreach (var id in conditionIds)
        {
            var c = ConditionCatalog.GetCondition(id);
            Conditions.Add(new ManageConditionChip { Id = c.Id, Name = c.Name, Icon = c.Icon });
        }
        RebuildConditionItems();

        CarePlanRows.Clear();
        foreach (var t in plan)
            CarePlanRows.Add(BuildRow(t));

        BuildPreview(plan, meds.Count);

        Medications.Clear();
        foreach (var m in meds)
            Medications.Add(new ManageMedRow
            {
                Id = m.Id,
                Name = m.Name,
                Detail = $"{m.Dosage.ToString("0.##", Ci)} {m.Unit}".Trim()
            });

        OnPropertyChanged(nameof(HasCarePlan));
        OnPropertyChanged(nameof(HasNoCarePlan));
    }

    private void ClearAll()
    {
        Conditions.Clear();
        RebuildConditionItems();
        CarePlanRows.Clear();
        PreviewChips.Clear();
        Medications.Clear();
        OnPropertyChanged(nameof(HasCarePlan));
        OnPropertyChanged(nameof(HasNoCarePlan));
    }

    // Add always sorts last so it wraps in the FlexLayout exactly like a chip would.
    private void RebuildConditionItems()
    {
        ConditionItems.Clear();
        foreach (var c in Conditions)
            ConditionItems.Add(c);
        ConditionItems.Add(AddConditionChipItem.Instance);
    }

    // ── Remove pet ───────────────────────────────────────────────────────────────
    // The owner's exit is deleting the pet; a caregiver's is leaving it (the sharing
    // sheet's Leave is the same operation). The page owns the native dialogs and the
    // export offer; this VM only decides which removal applies and runs it.
    private async Task OnRemovePetAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        var kind = _deletion.DetermineKind(pet);

        var flow = RequestRemoveFlow != null
            ? await RequestRemoveFlow(kind)
            : PetRemovalFlowResult.Cancel;

        // Cancelled, or the owner chose to save a copy first (the page opened the
        // export sheet) — either way there is nothing to remove right now.
        if (flow != PetRemovalFlowResult.Proceed)
            return;

        if (kind == PetRemovalKind.Caregiver)
        {
            await _deletion.LeaveSharedPetAsync(pet);
            PetRemoved?.Invoke(true);
            return;
        }

        var result = await _deletion.DeletePetAsync(pet);
        PetRemoved?.Invoke(result.AnyPetsRemain);
    }

    // ── Care-plan row build ──────────────────────────────────────────────────────
    private CarePlanRow BuildRow(Tracker t)
    {
        var (icon, bg, fg) = Visual(t.TrackerId);
        var from = t.FromCondition;
        return new CarePlanRow
        {
            TrackerId = t.TrackerId,
            FromCondition = from,
            Icon = icon,
            IconBackground = Color.FromArgb(bg),
            IconForeground = Color.FromArgb(fg),
            Title = Loc.GetString(LabelKey(t.TrackerId)),
            Description = Describe(t),
            FromLabel = string.IsNullOrEmpty(from) ? string.Empty : ConditionCatalog.GetCondition(from).Name
        };
    }

    private static string Describe(Tracker t)
    {
        string freq = t.Kind switch
        {
            TrackerKind.PerDay => Loc.Format("Manage_FreqPerDay", t.PerDayCount),
            TrackerKind.Daily => Loc.GetString("CondSetup_FreqDaily"),
            TrackerKind.Weekly => Loc.GetString("CondSetup_FreqWeekly"),
            TrackerKind.TwiceWeekly => Loc.GetString("CondSetup_FreqTwiceWeekly"),
            TrackerKind.AsNeeded => Loc.GetString("CondSetup_FreqAsNeeded"),
            TrackerKind.Event => Loc.GetString("Manage_Event"),
            _ => string.Empty
        };

        if (t.TrackerId == TrackerId.Glucose && t.Kind != TrackerKind.Event)
        {
            string target = t.TargetRange is { } r
                ? Loc.Format("Manage_GlucoseTarget", r.Lo.ToString("0.0", Ci), r.Hi.ToString("0.0", Ci))
                : Loc.GetString("Manage_NoTarget");
            freq = $"{freq} · {target}";
        }

        return freq;
    }

    private void BuildPreview(IReadOnlyList<Tracker> plan, int medCount)
    {
        PreviewChips.Clear();
        foreach (var t in plan)
        {
            if (t.Kind is TrackerKind.Event or TrackerKind.AsNeeded)
                continue;
            var (icon, _, _) = Visual(t.TrackerId);
            PreviewChips.Add(new PreviewChip { Icon = icon, Text = Loc.GetString(LabelKey(t.TrackerId)) });
        }

        if (medCount > 0)
            PreviewChips.Add(new PreviewChip { Icon = "💊", Text = Loc.Format("Manage_MedsCount", medCount) });
    }

    // ── Care-plan row tap: condition-derived → its setup sheet; default → adjust ──
    private async Task OnTapCarePlanRowAsync(CarePlanRow? row)
    {
        if (row == null)
            return;

        if (ConditionSetup.HasSheet(row.FromCondition))
        {
            RequestConditionSetup?.Invoke(row.FromCondition!);
            return;
        }

        await OpenAdjustSheetAsync(row.TrackerId);
    }

    // ── Add condition ────────────────────────────────────────────────────────────
    private void OpenAddConditionSheet()
    {
        var already = Conditions.Select(c => c.Id).ToHashSet();
        AddConditionOptions = ConditionCatalog.Conditions
            // Skip the "None" sentinel and anything already added.
            .Where(c => !string.IsNullOrEmpty(c.Id) && !already.Contains(c.Id))
            .Select(c => new AddConditionOption { Id = c.Id, Name = c.Name, Icon = c.Icon })
            .ToList();
        IsAddConditionSheetVisible = true;
    }

    private async Task OnPickAddConditionAsync(AddConditionOption? option)
    {
        if (option == null)
            return;

        IsAddConditionSheetVisible = false;

        // Conditions with a setup sheet open that sheet (the second "door"); the rest
        // are added straight away with whatever trackers they contribute.
        if (ConditionSetup.HasSheet(option.Id))
        {
            RequestConditionSetup?.Invoke(option.Id);
            return;
        }

        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        await _conditions.AddAsync(pet.Id, option.Id);
        await _trackers.EnsureSeededAsync(pet.Id, System.Array.Empty<string>());
        foreach (var seed in CarePlanCatalog.ForCondition(option.Id))
            await _trackers.UpsertAsync(pet.Id, seed.TrackerId, t =>
            {
                t.Kind = seed.Kind;
                t.PerDayCount = seed.PerDayCount;
                t.Unit = seed.Unit;
                t.FromCondition ??= option.Id;
            });

        await LoadAsync();
    }

    private bool _isAddConditionSheetVisible;
    public bool IsAddConditionSheetVisible
    {
        get => _isAddConditionSheetVisible;
        set => SetProperty(ref _isAddConditionSheetVisible, value);
    }

    // ── Remove condition ─────────────────────────────────────────────────────────
    private string _removeConditionId = string.Empty;

    private void OpenRemoveSheet(ManageConditionChip? chip)
    {
        if (chip == null)
            return;

        _removeConditionId = chip.Id;
        RemoveTitle = Loc.Format("Manage_RemoveTitle", chip.Name);

        var linked = CarePlanRows
            .Where(r => r.FromCondition == chip.Id)
            .Select(r => r.Title)
            .ToList();
        RemoveExplanation = linked.Count > 0
            ? Loc.Format("Manage_RemoveLinked", string.Join(", ", linked))
            : Loc.GetString("Manage_RemoveNoLinked");

        IsRemoveSheetVisible = true;
    }

    private async Task RemoveConditionAsync(bool alsoTrackers)
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0 || string.IsNullOrEmpty(_removeConditionId))
        {
            IsRemoveSheetVisible = false;
            return;
        }

        await _conditions.RemoveAsync(pet.Id, _removeConditionId);

        var linked = (await _trackers.GetForPetAsync(pet.Id))
            .Where(t => t.FromCondition == _removeConditionId)
            .ToList();

        foreach (var t in linked)
        {
            if (alsoTrackers)
                await _trackers.DeleteAsync(t);
            else
            {
                // Keep the tracker but sever its breadcrumb so it stands on its own.
                t.FromCondition = null;
                await _trackers.SaveAsync(t);
            }
        }

        IsRemoveSheetVisible = false;
        await LoadAsync();
    }

    private bool _isRemoveSheetVisible;
    public bool IsRemoveSheetVisible
    {
        get => _isRemoveSheetVisible;
        set => SetProperty(ref _isRemoveSheetVisible, value);
    }

    private string _removeTitle = string.Empty;
    public string RemoveTitle
    {
        get => _removeTitle;
        private set => SetProperty(ref _removeTitle, value);
    }

    private string _removeExplanation = string.Empty;
    public string RemoveExplanation
    {
        get => _removeExplanation;
        private set => SetProperty(ref _removeExplanation, value);
    }

    // ── Adjust a default tracker (Mood / Weight) ─────────────────────────────────
    private TrackerId _adjustTrackerId;

    private async Task OpenAdjustSheetAsync(TrackerId trackerId)
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        _adjustTrackerId = trackerId;
        AdjustTitle = Loc.GetString(LabelKey(trackerId));

        var current = await _trackers.GetByTrackerIdAsync(pet.Id, trackerId);
        var currentKind = current?.Kind ?? DefaultKind(trackerId);

        // Fresh list (not in-place mutation) — see the AdjustOptions field note.
        AdjustOptions = OptionsFor(trackerId)
            .Select(o => new AdjustOption
            {
                Kind = o.Item1,
                Label = Loc.GetString(o.Item2),
                IsSelected = o.Item1 == currentKind
            })
            .ToList();

        IsAdjustSheetVisible = true;
    }

    private void SelectAdjust(AdjustOption? option)
    {
        if (option == null)
            return;
        foreach (var o in AdjustOptions)
            o.IsSelected = ReferenceEquals(o, option);
    }

    private async Task SaveAdjustAsync()
    {
        var pet = _activePet.ActivePet;
        var chosen = AdjustOptions.FirstOrDefault(o => o.IsSelected);
        if (pet == null || pet.Id == 0 || chosen == null)
        {
            IsAdjustSheetVisible = false;
            return;
        }

        await _trackers.UpsertAsync(pet.Id, _adjustTrackerId, t => t.Kind = chosen.Kind);
        IsAdjustSheetVisible = false;
        await LoadAsync();
    }

    private async Task TurnOffTrackerAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
        {
            IsAdjustSheetVisible = false;
            return;
        }

        await _trackers.RemoveByTrackerIdAsync(pet.Id, _adjustTrackerId);
        IsAdjustSheetVisible = false;
        await LoadAsync();
    }

    private bool _isAdjustSheetVisible;
    public bool IsAdjustSheetVisible
    {
        get => _isAdjustSheetVisible;
        set => SetProperty(ref _isAdjustSheetVisible, value);
    }

    private string _adjustTitle = string.Empty;
    public string AdjustTitle
    {
        get => _adjustTitle;
        private set => SetProperty(ref _adjustTitle, value);
    }

    public string AdjustSubtitle => Loc.GetString("Manage_AdjustSub");

    // ── Static maps ──────────────────────────────────────────────────────────────
    private static TrackerKind DefaultKind(TrackerId id) => id switch
    {
        TrackerId.Weight => TrackerKind.Weekly,
        _ => TrackerKind.Daily
    };

    // The cadence options offered for a default tracker in the adjust sheet.
    private static IReadOnlyList<(TrackerKind, string)> OptionsFor(TrackerId id) => id switch
    {
        TrackerId.Weight => new[]
        {
            (TrackerKind.Daily, "CondSetup_FreqDaily"),
            (TrackerKind.TwiceWeekly, "CondSetup_FreqTwiceWeekly"),
            (TrackerKind.Weekly, "CondSetup_FreqWeekly"),
        },
        _ => new[]
        {
            (TrackerKind.Daily, "CondSetup_FreqDaily"),
            (TrackerKind.AsNeeded, "CondSetup_FreqAsNeeded"),
        }
    };

    private static string LabelKey(TrackerId id) => id switch
    {
        TrackerId.Glucose => "Journal_GlucoseCheck",
        TrackerId.Mood => "Journal_MoodTitle",
        TrackerId.Appetite => "Journal_Appetite",
        TrackerId.Weight => "Journal_WeighIn",
        TrackerId.Water => "Journal_Water",
        TrackerId.Seizure => "Journal_Seizure",
        _ => "Journal_MoodTitle"
    };

    // Rockpool icon + tint/deep per tracker (hex literals, per the app's convention).
    private static (string icon, string bg, string fg) Visual(TrackerId id) => id switch
    {
        TrackerId.Glucose => ("🩸", "#24BE5F76", "#8C3B50"),
        TrackerId.Mood => ("🙂", "#21149081", "#0C6A5D"),
        TrackerId.Appetite => ("🍽️", "#29D9973C", "#8A5D14"),
        TrackerId.Weight => ("⚖️", "#243E8FB0", "#2A6E8C"),
        TrackerId.Water => ("💧", "#243E8FB0", "#2A6E8C"),
        TrackerId.Seizure => ("⚡", "#267B6BAE", "#584A8A"),
        _ => ("•", "#57FFFFFF", "#0D3A3C")
    };
}
