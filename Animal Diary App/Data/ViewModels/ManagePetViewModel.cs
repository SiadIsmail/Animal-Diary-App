namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
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
    /// <summary>Which tracker this row edits — a shipped one or one the owner made up.</summary>
    public TrackerKey Key { get; init; }
    public string? FromCondition { get; init; }
    public string Icon { get; init; } = string.Empty;
    public Color IconBackground { get; init; } = Colors.Transparent;
    public Color IconForeground { get; init; } = Colors.Black;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FromLabel { get; init; } = string.Empty;
    public bool HasFrom => !string.IsNullOrEmpty(FromLabel);
}

/// <summary>A medication row in the Manage page's Medications section.</summary>
public class ManageMedRow
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>A condition offered in the "add condition" sheet.</summary>
/// <summary>A log type the pet's care plan doesn't cover yet, offered in the
/// "Add a tracker" sheet.</summary>
public class AddTrackerOption
{
    /// <summary>Which shipped tracker this offers, or null for the "add your own" row —
    /// the one option that opens the custom sheet instead of adding a tracker outright.</summary>
    public TrackerId? TrackerId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;

    /// <summary>The last row: "something else", which starts the owner's own tracker.</summary>
    public bool IsCustom => TrackerId is null;
}

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

    /// <summary>Checks per day when <see cref="Kind"/> is <see cref="TrackerKind.PerDay"/>;
    /// 0 otherwise. Carried on the option so a multi-times-a-day cadence can be offered
    /// as its own row ("3× daily") without the sheet needing a separate counter.</summary>
    public int PerDayCount { get; init; }

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
    private readonly CustomTrackerService _custom;
    private readonly MedicationService _medications;
    private readonly PetDeletionService _deletion;
    private readonly PetPauseService _pause;
    private readonly MedicationReminderScheduler _reminders;
    private readonly DailyCareReminderScheduler _dailyReminders;

    public ManagePetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers,
        CarePlanService carePlan,
        CustomTrackerService custom,
        MedicationService medications,
        PetDeletionService deletion,
        PetPauseService pause,
        MedicationReminderScheduler reminders,
        DailyCareReminderScheduler dailyReminders)
    {
        _activePet = activePet;
        _conditions = conditions;
        _trackers = trackers;
        _carePlan = carePlan;
        _custom = custom;
        _medications = medications;
        _deletion = deletion;
        _pause = pause;
        _reminders = reminders;
        _dailyReminders = dailyReminders;

        TapIdentityCommand = new Command(() => RequestEditPet?.Invoke());
        AddConditionCommand = new Command(OpenAddConditionSheet);
        AddTrackerCommand = new Command(OpenAddTrackerSheet);
        PickAddTrackerCommand = new Command<AddTrackerOption>(async o => await OnPickAddTrackerAsync(o));
        CloseAddTrackerCommand = new Command(() => IsAddTrackerSheetVisible = false);
        RemoveConditionCommand = new Command<ManageConditionChip>(OpenRemoveSheet);
        TapCarePlanRowCommand = new Command<CarePlanRow>(async r => await OnTapCarePlanRowAsync(r));
        AddMedicationCommand = new Command(() => RequestAddMedication?.Invoke());
        TapMedicationCommand = new Command<ManageMedRow>(m => { if (m != null) RequestOpenMedication?.Invoke(m.Id); });

        OpenConditionCommand = new Command<ManageConditionChip>(OnOpenCondition);
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
        TogglePauseCommand = new Command(async () => await OnTogglePauseAsync());
    }

    // ── Events the page acts on ──────────────────────────────────────────────────
    /// <summary>Open the reusable setup sheet for this condition id (diabetes/ckd/epilepsy).</summary>
    public event Action<string>? RequestConditionSetup;

    /// <summary>Open a tracker's own editor (Glucose, Seizure). Distinct from
    /// <see cref="RequestConditionSetup"/> because it reuses the same sheets WITHOUT
    /// linking their condition — editing a target range must not record a diagnosis.</summary>
    public event Action<TrackerId>? RequestTrackerSetup;

    /// <summary>Open the owner's own tracker sheet: the row to edit, or null to create
    /// one. The single door to every custom tracker — there is no per-tracker editor.</summary>
    public event Action<CustomTracker?>? RequestCustomTracker;

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

    /// <summary>Raised right after a pet is paused, so the page can quietly offer the
    /// export once (AI/app-voice.md §15.5). The page owns the native offer; the VM
    /// doesn't care about the answer.</summary>
    public event Action? RequestPauseExportOffer;

    // ── Bindable lists ───────────────────────────────────────────────────────────
    public ObservableCollection<ManageConditionChip> Conditions { get; } = new();

    /// <summary>What the Conditions FlexLayout actually renders: every condition chip
    /// followed by the Add chip, rebuilt whenever <see cref="Conditions"/> changes so
    /// Add always sorts last and wraps like any other chip.</summary>
    public ObservableCollection<IConditionChipItem> ConditionItems { get; } = new();
    public ObservableCollection<CarePlanRow> CarePlanRows { get; } = new();
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

    public bool HasCarePlan => CarePlanRows.Count > 0;
    public bool HasNoCarePlan => CarePlanRows.Count == 0;
    public string EmptyPlanText => Loc.GetString("Manage_EmptyPlan");

    // ── Commands ─────────────────────────────────────────────────────────────────
    public ICommand TapIdentityCommand { get; }
    public ICommand AddConditionCommand { get; }

    /// <summary>Tapping a condition chip's name reopens that condition's setup, so it
    /// can be reconfigured from the condition itself. Conditions with nothing to
    /// configure (no setup sheet) do nothing — there is no screen to show.</summary>
    public ICommand OpenConditionCommand { get; }
    public ICommand AddTrackerCommand { get; }
    public ICommand PickAddTrackerCommand { get; }
    public ICommand CloseAddTrackerCommand { get; }
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
    public ICommand TogglePauseCommand { get; }

    /// <summary>
    /// Label for the destructive remove row. It must say what the action ACTUALLY does for
    /// this viewer: an owner deletes the pet, a caregiver only leaves it. The dialogs behind
    /// the row already branch on role, but the row itself used to read "Remove Charly from
    /// Felova" for everyone — telling a caregiver they were about to delete someone else's
    /// animal, which is both false and frightening.
    /// </summary>
    public string RemovePetLabel => IsCaregiverRemoval
        ? Loc.Format("Manage_LeavePetRow", PetName)
        : Loc.Format("Manage_RemovePetRow", PetName);

    /// <summary>
    /// The row's second line: what removal actually costs this viewer. It branches on the
    /// same role as the label, because the two answers are not softenings of each other —
    /// an owner loses the record, a caregiver only loses their view of it. Saying "can't be
    /// undone" to someone who is merely stepping away would be a lie in the frightening
    /// direction (§14: no hierarchy theatre, so neither line names a role).
    /// </summary>
    public string RemovePetSubtitle => IsCaregiverRemoval
        ? Loc.Format("Manage_LeavePetSubtitle", PetName)
        : Loc.GetString("Manage_RemovePetSubtitle");

    /// <summary>True when this viewer would only *leave* the pet rather than delete it.</summary>
    private bool IsCaregiverRemoval
    {
        get
        {
            var pet = _activePet.ActivePet;
            return pet != null && pet.Id != 0 && _deletion.DetermineKind(pet) == PetRemovalKind.Caregiver;
        }
    }

    // ── Pause everything (AI/app-voice.md §15) ───────────────────────────────────
    // Per-device (PetPauseService): pausing stops every reminder for this pet on this
    // device and keeps the whole record. It never touches data and never syncs — one
    // carer stepping back doesn't silence the other's reminders.
    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
                OnPropertyChanged(nameof(PauseRowSubtitle));
        }
    }

    /// <summary>
    /// Row label, fixed: "Pause everything for Charly". It does NOT flip to "Resume…" —
    /// the row carries a Switch bound to <see cref="IsPaused"/>, and a switch labelled with
    /// the opposite of its own state is unreadable ("Resume everything", on). The label names
    /// the thing being switched; the switch says whether it's on.
    /// </summary>
    public string PauseRowLabel => Loc.Format("Manage_PauseRow", PetName);

    /// <summary>
    /// The row's second line, which is where the state is spelled out in words (§15: plain,
    /// no euphemism). Paused, it is the status line; unpaused, it is what the switch would do.
    /// This replaced a free-floating notice label that only existed while paused.
    /// </summary>
    public string PauseRowSubtitle => IsPaused
        ? Loc.Format("Manage_PausedNotice", PetName)
        : Loc.GetString("Manage_PauseSubtitle");

    // ── Load ─────────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        // Refresh identity text.
        OnPropertyChanged(nameof(PetName));
        OnPropertyChanged(nameof(PetInitial));
        OnPropertyChanged(nameof(PetSubtitle));
        OnPropertyChanged(nameof(IdentityHint));
        OnPropertyChanged(nameof(EmptyPlanText));
        OnPropertyChanged(nameof(RemovePetLabel));
        OnPropertyChanged(nameof(RemovePetSubtitle));

        var pet = _activePet.ActivePet;

        // Reflect this device's pause state for the active pet. The setter only fires when
        // the state actually changed, so the label and subtitle are raised unconditionally
        // below — both carry the pet's name, which changes when the active pet does.
        IsPaused = pet != null && pet.Id != 0 && _pause.IsPaused(pet.Id);
        OnPropertyChanged(nameof(PauseRowLabel));
        OnPropertyChanged(nameof(PauseRowSubtitle));

        if (pet == null || pet.Id == 0)
        {
            ClearAll();
            return;
        }

        // Gather everything before touching the observable collections.
        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        var plan = (await _carePlan.GetPlanAsync(pet)).ToList();
        // The owner's own definitions, so a custom row can render its name, emoji and
        // colour. The plan carries only cadence — the identity lives on the row.
        _customById = (await _custom.GetForPetAsync(pet.Id)).ToDictionary(c => c.Id);
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

        // The row stays while there is anything left to add — a shipped tracker not yet
        // in the plan, OR room for one more of the owner's own. Counting only the shipped
        // ones would hide "add your own" the moment a pet used all six.
        CanAddTracker = CarePlanRows.Count(r => !r.Key.IsCustom) < System.Enum.GetValues<TrackerId>().Length
            || _customById.Count < CustomTracker.MaxPerPet;

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
        // The definitions belong to the pet that just went away — keeping them would let
        // the "add your own" cap be judged against another pet's trackers.
        _customById = new Dictionary<int, CustomTracker>();
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

    // ── Pause / resume everything for this pet ───────────────────────────────────
    private async Task OnTogglePauseAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        if (IsPaused)
        {
            // Resume: clear the flag, then re-arm reminders from the saved schedules.
            _pause.Resume(pet.Id);
            IsPaused = false;
            await _reminders.SyncPetAsync(pet.Id);
            await _dailyReminders.RefreshAsync();
            return;
        }

        // Pause: set the flag first so any concurrent catch-up already skips this pet,
        // then cancel everything already armed. §15.1 — one tap stops everything.
        _pause.Pause(pet.Id);
        IsPaused = true;
        await _reminders.CancelPetAsync(pet.Id);
        await _dailyReminders.RefreshAsync();

        // Offer the export once, quietly (§15.5). Never deletes anything.
        RequestPauseExportOffer?.Invoke();
    }

    // ── Care-plan row build ──────────────────────────────────────────────────────
    /// <summary>The pet's own tracker definitions by row id, for rendering custom
    /// care-plan rows. Refilled by every <see cref="LoadAsync"/>.</summary>
    private Dictionary<int, CustomTracker> _customById = new();

    private CarePlanRow BuildRow(CarePlanItem t)
    {
        // A custom line takes its name, emoji and colour from the owner's own row; a
        // shipped one from the static table. The name is USER TEXT — shown verbatim,
        // never passed through the localizer.
        if (t.Key.IsCustom)
        {
            var def = _customById.GetValueOrDefault(t.Key.CustomId);
            var v = CustomTrackerVisuals.For(def);
            return new CarePlanRow
            {
                Key = t.Key,
                Icon = v.Icon,
                IconBackground = AppColors.Resolve(v.RowTintKey),
                IconForeground = AppColors.Resolve(v.RowInkKey),
                Title = def?.Name ?? string.Empty,
                Description = Describe(t),
            };
        }

        var (icon, bg, fg) = Visual(t.Key.BuiltIn);
        var from = t.FromCondition;
        return new CarePlanRow
        {
            Key = t.Key,
            FromCondition = from,
            Icon = icon,
            IconBackground = bg,
            IconForeground = fg,
            Title = Loc.GetString(LabelKey(t.Key.BuiltIn)),
            Description = Describe(t),
            FromLabel = string.IsNullOrEmpty(from) ? string.Empty : ConditionCatalog.GetCondition(from).Name
        };
    }

    private static string Describe(CarePlanItem t)
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

        // A custom tracker's unit is the only thing its row adds beyond the cadence, and
        // only when it records a number: "Once a day · min".
        if (t.Key.IsCustom)
            return string.IsNullOrWhiteSpace(t.Unit) ? freq : $"{freq} · {t.Unit}";

        if (t.Key.Is(TrackerId.Glucose) && t.Kind != TrackerKind.Event)
        {
            string target = t.TargetRange is { } r
                ? Loc.Format("Manage_GlucoseTarget", r.Lo.ToString("0.0", Ci), r.Hi.ToString("0.0", Ci))
                : Loc.GetString("Manage_NoTarget");
            freq = $"{freq} · {target}";
        }

        return freq;
    }

    // ── Care-plan row tap: condition-derived → its setup sheet; default → adjust ──
    private async Task OnTapCarePlanRowAsync(CarePlanRow? row)
    {
        if (row == null)
            return;

        // Glucose and Seizure always open their own editor, condition or not. Both hold
        // settings the plain cadence picker can't express — a target range, and "as it
        // happens" — and both are properties of the measurement rather than of a
        // diagnosis. Routing on the tracker instead of on FromCondition also means a
        // hand-added Glucose can no longer land in a picker that offers neither its
        // per-day frequency nor its range, and silently overwrite both on save.
        // The sheets are opened WITHOUT linking their condition: see LinkCondition.
        // A custom tracker has no shipped editor and no condition behind it — the one
        // custom sheet owns its name, look, shape and cadence together, so it is checked
        // before everything else.
        if (row.Key.IsCustom)
        {
            if (_customById.TryGetValue(row.Key.CustomId, out var def))
                RequestCustomTracker?.Invoke(def);
            return;
        }

        if (row.Key.BuiltIn is not TrackerId builtIn)
            return;

        if (builtIn is TrackerId.Glucose or TrackerId.Seizure)
        {
            RequestTrackerSetup?.Invoke(builtIn);
            return;
        }

        if (ConditionSetup.HasSheet(row.FromCondition))
        {
            RequestConditionSetup?.Invoke(row.FromCondition!);
            return;
        }

        await OpenAdjustSheetAsync(builtIn);
    }

    private void OnOpenCondition(ManageConditionChip? chip)
    {
        // Reconfiguring here DOES link the condition (it is already linked — this is the
        // condition's own door), unlike the tracker-row door which must not.
        if (chip != null && ConditionSetup.HasSheet(chip.Id))
            RequestConditionSetup?.Invoke(chip.Id);
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
            // isNew — see ConditionPickerViewModel: a tracker the owner added on their
            // own is never claimed by a condition added afterwards.
            await _trackers.UpsertAsync(pet.Id, seed.TrackerId, (t, isNew) =>
            {
                t.Kind = seed.Kind;
                t.PerDayCount = seed.PerDayCount;
                t.Unit = seed.Unit;
                if (isNew)
                    t.FromCondition = option.Id;
            });

        await LoadAsync();
    }

    private bool _isAddConditionSheetVisible;
    public bool IsAddConditionSheetVisible
    {
        get => _isAddConditionSheetVisible;
        set => SetProperty(ref _isAddConditionSheetVisible, value);
    }

    // ── Add a tracker ────────────────────────────────────────────────────────────
    // The deliberate half of the pair the Journal's "+" completes. There, picking a
    // log type outside the plan records it once and commits to nothing; here, the
    // owner is saying "keep asking me about this", so it joins the care plan and the
    // "still to do" row. Conditions were only ever a shortcut for filling this in —
    // wanting to note water shouldn't require claiming a kidney diagnosis.
    private void OpenAddTrackerSheet()
    {
        var already = CarePlanRows.Select(r => r.Key.BuiltIn).ToHashSet();
        var options = System.Enum.GetValues<TrackerId>()
            .Where(id => !already.Contains(id))
            .Select(id =>
            {
                var (icon, _, _) = Visual(id);
                return new AddTrackerOption { TrackerId = id, Name = Loc.GetString(LabelKey(id)), Icon = icon };
            })
            .ToList();

        // "Something else" sits LAST and always, unless the pet is already at the cap.
        // Last because the shipped six answer most of what people want and need no
        // setup; always because the whole point is that the list is not the limit.
        if (_customById.Count < CustomTracker.MaxPerPet)
            options.Add(new AddTrackerOption
            {
                TrackerId = null,
                Name = Loc.GetString("Custom_AddOwn"),
                Icon = "✎",
            });

        AddTrackerOptions = options;
        IsAddTrackerSheetVisible = true;
    }

    private async Task OnPickAddTrackerAsync(AddTrackerOption? option)
    {
        if (option == null)
            return;

        IsAddTrackerSheetVisible = false;

        // "Something else" hands straight over to the custom sheet — nothing is created
        // until the owner has named it, so backing out of that sheet leaves no trace.
        if (option.TrackerId is not TrackerId trackerId)
        {
            RequestCustomTracker?.Invoke(null);
            return;
        }

        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        // Same cadence a condition would have given it, so a hand-added tracker and a
        // seeded one are indistinguishable once they exist. FromCondition stays null:
        // this is the owner's own choice, and removing a condition must never take it.
        var seed = CarePlanCatalog.DefaultFor(trackerId);
        await _trackers.UpsertAsync(pet.Id, trackerId, (t, _) =>
        {
            t.Kind = seed.Kind;
            t.PerDayCount = seed.PerDayCount;
            t.Unit = seed.Unit;
        });

        await LoadAsync();
    }

    private IReadOnlyList<AddTrackerOption> _addTrackerOptions = System.Array.Empty<AddTrackerOption>();
    public IReadOnlyList<AddTrackerOption> AddTrackerOptions
    {
        get => _addTrackerOptions;
        private set => SetProperty(ref _addTrackerOptions, value);
    }

    private bool _isAddTrackerSheetVisible;
    public bool IsAddTrackerSheetVisible
    {
        get => _isAddTrackerSheetVisible;
        set => SetProperty(ref _isAddTrackerSheetVisible, value);
    }

    /// <summary>Whether any log type is still missing from this pet's plan. The row is
    /// hidden once they're all in, rather than opening an empty sheet.</summary>
    private bool _canAddTracker = true;
    public bool CanAddTracker
    {
        get => _canAddTracker;
        private set => SetProperty(ref _canAddTracker, value);
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
        var defaults = CarePlanCatalog.DefaultFor(trackerId);
        var currentKind = current?.Kind ?? defaults.Kind;
        var currentPerDay = current?.PerDayCount ?? defaults.PerDayCount;

        // Fresh list (not in-place mutation) — see the AdjustOptions field note.
        // A PerDay rung has to match on the COUNT too, or "3× daily" and "2× daily"
        // would both light up for the same tracker.
        var options = OptionsFor(trackerId)
            .Select(o => new AdjustOption
            {
                Kind = o.Kind,
                PerDayCount = o.PerDay,
                Label = CadenceLabel(o.Kind, o.PerDay),
                IsSelected = o.Kind == currentKind
                             && (o.Kind != TrackerKind.PerDay || o.PerDay == currentPerDay)
            })
            .ToList();

        // The tracker's CURRENT cadence is always offered, even if the ladder above
        // wouldn't propose it. Without this the sheet can open with nothing selected,
        // and then any save silently rewrites the tracker to a cadence the owner never
        // had — which is how a seizure log could become a daily chore. The invariant
        // to keep: every setting reachable by SaveAdjustAsync is one the sheet showed.
        if (options.All(o => !o.IsSelected))
        {
            options.Insert(0, new AdjustOption
            {
                Kind = currentKind,
                PerDayCount = currentPerDay,
                Label = CadenceLabel(currentKind, currentPerDay),
                IsSelected = true
            });
        }

        AdjustOptions = options;
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

        await _trackers.UpsertAsync(pet.Id, _adjustTrackerId, t =>
        {
            t.Kind = chosen.Kind;
            // Cleared when leaving PerDay, or a stale count outlives the cadence that
            // gave it meaning and the row reads "0× daily" if it ever comes back.
            t.PerDayCount = chosen.Kind == TrackerKind.PerDay ? chosen.PerDayCount : 0;
        });
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
    /// <summary>Cadence for a tracker with no stored row yet. Delegates to the catalog
    /// so this can't drift from what a bare add or a condition seed would produce —
    /// the local copy used to answer "Daily" for Seizure, which is an Event.</summary>
    private static TrackerKind DefaultKind(TrackerId id) => CarePlanCatalog.DefaultFor(id).Kind;

    // The cadence ladder, offered per tracker in the adjust sheet.
    //
    // Every rung the pending engine actually understands is on offer here: several
    // times a day, once a day, a couple of times a week, weekly, or never asked for.
    // Rungs are only withheld where they'd be meaningless for that particular thing —
    // weighing a pet three times a day isn't a routine, it's a fixation, and mood is
    // one reading of a day rather than a series of them.
    //
    // Glucose (PerDay + a target range) and Seizure (Event) never reach here: they have
    // their own editors, because neither fits a plain single-select list.
    private static IReadOnlyList<(TrackerKind Kind, int PerDay)> OptionsFor(TrackerId id) => id switch
    {
        // Meals and drinking happen several times a day, so those rungs are real here.
        TrackerId.Appetite or TrackerId.Water => new[]
        {
            (TrackerKind.PerDay, 3),
            (TrackerKind.PerDay, 2),
            (TrackerKind.Daily, 0),
            (TrackerKind.TwiceWeekly, 0),
            (TrackerKind.Weekly, 0),
            (TrackerKind.AsNeeded, 0),
        },
        _ => new[]
        {
            (TrackerKind.Daily, 0),
            (TrackerKind.TwiceWeekly, 0),
            (TrackerKind.Weekly, 0),
            (TrackerKind.AsNeeded, 0),
        }
    };

    /// <summary>One cadence written out. Also used for the safety row that shows a
    /// tracker's current setting when the ladder above wouldn't have offered it.</summary>
    private static string CadenceLabel(TrackerKind kind, int perDayCount) =>
        kind == TrackerKind.PerDay
            ? Loc.Format("Manage_FreqPerDay", perDayCount)
            : Loc.GetString(kind switch
            {
                TrackerKind.Daily => "CondSetup_FreqDaily",
                TrackerKind.TwiceWeekly => "CondSetup_FreqTwiceWeekly",
                TrackerKind.Weekly => "CondSetup_FreqWeekly",
                TrackerKind.AsNeeded => "CondSetup_FreqAsNeeded",
                _ => "Manage_Event"
            });

    // Nullable, because a care-plan line may be a custom tracker, which has no entry in
    // the shipped table — those fall to TrackerVisuals.Fallback until Phase 2 builds a
    // visual from the owner's own row.
    private static string LabelKey(TrackerId? id) => TrackerVisuals.For(id).LabelKey;

    // Rockpool icon + row tint/ink per tracker, from the shared TrackerVisuals table
    // and resolved against Colors.xaml — never hex literals, which no theme can reach.
    private static (string icon, Color bg, Color fg) Visual(TrackerId? id)
    {
        var v = TrackerVisuals.For(id);
        return (v.Icon, AppColors.Resolve(v.RowTintKey), AppColors.Resolve(v.RowInkKey));
    }
}
