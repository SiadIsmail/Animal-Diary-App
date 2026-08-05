namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One emoji in the sheet's icon row.</summary>
public class IconChoice : BaseViewModel
{
    public string Icon { get; init; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>One colour in the sheet's swatch row. Both colours are resolved through
/// <see cref="AppColors"/>, never built from a literal.</summary>
public class ColorChoice : BaseViewModel
{
    public string Key { get; init; } = string.Empty;
    public Color Fill { get; init; } = Colors.Transparent;
    public Color Ink { get; init; } = Colors.Black;

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>One starting point offered when the sheet opens empty.</summary>
public class PresetChoice
{
    public CustomTrackerPresets.Preset Preset { get; init; }
    public string Icon { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Create or edit a tracker the owner defines: a name, an emoji, a colour, whether it
/// records a number, and how often the Journal should ask.
///
/// <para>This is the <b>one</b> sheet behind every owner-created tracker — there is no
/// per-tracker editor to write ever again. Its shipped counterparts are the condition
/// setup sheets (<c>DiabetesSetupSheetViewModel</c> and friends), which configure the
/// six trackers the app knows by name.</para>
///
/// <para>It does not log anything. Writing an entry is the Journal's job; this decides
/// what exists and what gets asked for, exactly like the Manage page's other sheets.</para>
/// </summary>
public class CustomTrackerSheetViewModel : BaseViewModel
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private readonly ActivePetService _activePet;
    private readonly CustomTrackerService _custom;

    /// <summary>The row being edited, or null when creating. Held rather than re-read on
    /// save so an edit can't silently resurrect a tracker deleted from another device
    /// mid-sheet — <see cref="CustomTrackerService.SaveAsync"/> updates by id.</summary>
    private CustomTracker? _editing;

    public CustomTrackerSheetViewModel(ActivePetService activePet, CustomTrackerService custom)
    {
        _activePet = activePet;
        _custom = custom;

        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
        PickIconCommand = new Command<IconChoice>(PickIcon);
        PickColorCommand = new Command<ColorChoice>(PickColor);
        PickPresetCommand = new Command<PresetChoice>(ApplyPreset);
        PickCadenceCommand = new Command<AdjustOption>(PickCadence);
        SetTickCommand = new Command(() => IsAmount = false);
        SetAmountCommand = new Command(() => IsAmount = true);
        RetireCommand = new Command(async () => await RetireAsync());
    }

    /// <summary>Raised after a save or a retire, so the Manage page reloads its plan.</summary>
    public event Action? Changed;

    /// <summary>Ask the page to confirm a retire (native alert — two outcomes, so not a
    /// sheet; see AI/coding-standards.md).
    ///
    /// <para>It needs asking because there is no way back: retiring drops the tracker out
    /// of the plan and the "+" sheet, and nothing in the app lists retired ones to bring
    /// back. Every other removal in Felova is either undoable (the toast) or confirmed
    /// (removing a pet); this one cannot be the exception. What the confirm must say is
    /// the reassuring half — the entries are all still there.</para></summary>
    public Func<Task<bool>>? ConfirmRetire;

    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand PickIconCommand { get; }
    public ICommand PickColorCommand { get; }
    public ICommand PickPresetCommand { get; }
    public ICommand PickCadenceCommand { get; }
    public ICommand SetTickCommand { get; }
    public ICommand SetAmountCommand { get; }
    public ICommand RetireCommand { get; }

    // ── Presentation ────────────────────────────────────────────────────────────
    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    public string Title => Loc.GetString(IsEditing ? "Custom_EditTitle" : "Custom_AddTitle");
    public string Subtitle => Loc.GetString(IsEditing ? "Custom_EditSub" : "Custom_AddSub");

    private bool _isEditing;
    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (!SetProperty(ref _isEditing, value))
                return;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(ShowPresets));
        }
    }

    /// <summary>Starting points are offered only when creating. Showing them over a
    /// tracker that already exists would invite one tap to overwrite the owner's own
    /// wording with app chrome.</summary>
    public bool ShowPresets => !IsEditing;

    // ── The form ────────────────────────────────────────────────────────────────
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(CanSave));
        }
    }

    /// <summary>Length cap, enforced on save rather than by silently truncating.
    /// The name rides a chip and a care-plan row, and German runs 10–30% longer than
    /// English (AI/app-voice.md §19) — so this is the layout's budget, not a data limit.</summary>
    public const int MaxNameLength = 24;

    /// <summary>Empty until the owner has typed something unusable; bound through
    /// <c>StringToBoolConverter</c> like every other validation line in the app.</summary>
    private string _nameError = string.Empty;
    public string NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    private bool _isAmount;
    public bool IsAmount
    {
        get => _isAmount;
        set
        {
            if (!SetProperty(ref _isAmount, value))
                return;
            OnPropertyChanged(nameof(IsTick));
            OnPropertyChanged(nameof(ShowUnit));
        }
    }

    public bool IsTick => !IsAmount;

    /// <summary>The unit field belongs to the Amount shape only — a groom has no unit.</summary>
    public bool ShowUnit => IsAmount;

    private string _unit = string.Empty;
    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value);
    }

    /// <summary>Whether this tracker's entries reach the vet summary — see
    /// <see cref="CustomTracker.IncludeInReport"/> for why it is asked here, once, and
    /// why it starts on.</summary>
    private bool _inReport = true;
    public bool InReport
    {
        get => _inReport;
        set => SetProperty(ref _inReport, value);
    }

    public ObservableCollection<IconChoice> Icons { get; } = new();
    public ObservableCollection<ColorChoice> Colors { get; } = new();
    public ObservableCollection<PresetChoice> Presets { get; } = new();

    /// <summary>The cadence ladder — the same rungs and the same chip control the shipped
    /// adjust sheet uses, so a custom tracker is scheduled in the app's existing
    /// vocabulary and the pending engine needs no new rule.</summary>
    public ObservableCollection<AdjustOption> Cadences { get; } = new();

    // ── Open ────────────────────────────────────────────────────────────────────

    /// <summary>Open empty, to create a tracker.</summary>
    public void OpenNew()
    {
        _editing = null;
        IsEditing = false;
        Name = string.Empty;
        NameError = string.Empty;
        Unit = string.Empty;
        IsAmount = false;
        InReport = true;
        BuildChoices(CustomTrackerVisuals.DefaultIcon, CustomTrackerVisuals.DefaultColorKey);
        BuildCadences(TrackerKind.Daily, 0);
        IsPresented = true;
    }

    /// <summary>Open on an existing tracker.</summary>
    public void OpenEdit(CustomTracker tracker)
    {
        _editing = tracker;
        IsEditing = true;
        Name = tracker.Name;
        NameError = string.Empty;
        Unit = tracker.Unit;
        IsAmount = tracker.Shape == CustomShape.Amount;
        InReport = tracker.IncludeInReport;
        BuildChoices(tracker.Icon, tracker.ColorKey);
        BuildCadences(tracker.Kind, tracker.PerDayCount);
        IsPresented = true;
    }

    private void BuildChoices(string icon, string colorKey)
    {
        var chosenIcon = string.IsNullOrWhiteSpace(icon) ? CustomTrackerVisuals.DefaultIcon : icon;

        Icons.Clear();
        foreach (var e in CustomTrackerVisuals.Icons)
            Icons.Add(new IconChoice { Icon = e, IsSelected = e == chosenIcon });

        // An icon saved by a newer build (or a preset's, which need not be in the row)
        // still has to be selectable, so it joins the front of the list rather than
        // leaving the sheet with nothing lit.
        if (Icons.All(i => !i.IsSelected))
            Icons.Insert(0, new IconChoice { Icon = chosenIcon, IsSelected = true });

        var swatch = CustomTrackerVisuals.SwatchFor(colorKey);
        Colors.Clear();
        foreach (var s in CustomTrackerVisuals.Palette)
            Colors.Add(new ColorChoice
            {
                Key = s.Key,
                Fill = AppColors.Resolve(s.RowTintKey),
                Ink = AppColors.Resolve(s.RowInkKey),
                IsSelected = s.Key == swatch.Key,
            });

        Presets.Clear();
        foreach (var p in CustomTrackerPresets.All)
            Presets.Add(new PresetChoice
            {
                Preset = p,
                Icon = p.Icon,
                Name = CustomTrackerPresets.Name(p),
            });
    }

    // The ladder a custom tracker may be set to. Every rung the pending engine
    // understands is here, including the two that never nag — "whenever it happens"
    // (Event) is the honest setting for a poop or a sick episode, and the app must never
    // put those on a to-do list.
    private static readonly (TrackerKind Kind, int PerDay)[] CadenceLadder =
    {
        (TrackerKind.PerDay, 3),
        (TrackerKind.PerDay, 2),
        (TrackerKind.Daily, 0),
        (TrackerKind.TwiceWeekly, 0),
        (TrackerKind.Weekly, 0),
        (TrackerKind.AsNeeded, 0),
        (TrackerKind.Event, 0),
    };

    private void BuildCadences(TrackerKind kind, int perDay)
    {
        Cadences.Clear();
        foreach (var (k, n) in CadenceLadder)
            Cadences.Add(new AdjustOption
            {
                Kind = k,
                PerDayCount = n,
                Label = CadenceLabel(k, n),
                IsSelected = k == kind && (k != TrackerKind.PerDay || n == perDay),
            });

        // Same invariant the shipped adjust sheet keeps: every setting a save can write
        // is one the sheet displayed, so a save can never rewrite the tracker to a
        // cadence the owner never chose.
        if (Cadences.All(c => !c.IsSelected))
            Cadences.Insert(0, new AdjustOption
            {
                Kind = kind,
                PerDayCount = perDay,
                Label = CadenceLabel(kind, perDay),
                IsSelected = true,
            });
    }

    private static string CadenceLabel(TrackerKind kind, int perDay) => kind switch
    {
        TrackerKind.PerDay => Loc.Format("Manage_FreqPerDay", perDay),
        TrackerKind.Daily => Loc.GetString("CondSetup_FreqDaily"),
        TrackerKind.TwiceWeekly => Loc.GetString("CondSetup_FreqTwiceWeekly"),
        TrackerKind.Weekly => Loc.GetString("CondSetup_FreqWeekly"),
        TrackerKind.AsNeeded => Loc.GetString("CondSetup_FreqAsNeeded"),
        _ => Loc.GetString("Manage_Event"),
    };

    // ── Picks ───────────────────────────────────────────────────────────────────
    // One pass that sets one and clears the rest — never "clear the old, set the new"
    // in two places (AI/coding-standards.md).

    private void PickIcon(IconChoice? choice)
    {
        if (choice == null) return;
        foreach (var i in Icons) i.IsSelected = ReferenceEquals(i, choice);
    }

    private void PickColor(ColorChoice? choice)
    {
        if (choice == null) return;
        foreach (var c in Colors) c.IsSelected = ReferenceEquals(c, choice);
    }

    private void PickCadence(AdjustOption? choice)
    {
        if (choice == null) return;
        foreach (var c in Cadences) c.IsSelected = ReferenceEquals(c, choice);
    }

    /// <summary>Fill the form from a starting point. It writes into the SAME fields the
    /// owner types into, so every part of it stays theirs to change — a preset is never
    /// saved as such and nothing downstream can tell one was used.</summary>
    private void ApplyPreset(PresetChoice? choice)
    {
        if (choice == null) return;
        var p = choice.Preset;

        Name = CustomTrackerPresets.Name(p);
        NameError = string.Empty;
        IsAmount = p.Shape == CustomShape.Amount;
        Unit = CustomTrackerPresets.Unit(p);
        InReport = p.InReport;
        BuildChoices(p.Icon, p.ColorKey);
        BuildCadences(p.Kind, p.PerDayCount);
    }

    // ── Save / retire ───────────────────────────────────────────────────────────

    private async Task SaveAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        var name = (Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            NameError = Loc.GetString("Custom_NameRequired");
            return;
        }
        if (name.Length > MaxNameLength)
        {
            NameError = Loc.Format("Custom_NameTooLong", MaxNameLength);
            return;
        }

        // The cap is checked on CREATE only: an existing tracker must always stay
        // editable, even if the limit later moved beneath it.
        if (_editing == null && await _custom.CountForPetAsync(pet.Id) >= CustomTrackerService.MaxPerPet)
        {
            NameError = Loc.Format("Custom_TooMany", CustomTrackerService.MaxPerPet);
            return;
        }

        var cadence = Cadences.FirstOrDefault(c => c.IsSelected);
        var row = _editing ?? new CustomTracker { PetId = pet.Id };

        row.PetId = pet.Id;
        row.Name = name;
        row.Icon = Icons.FirstOrDefault(i => i.IsSelected)?.Icon ?? CustomTrackerVisuals.DefaultIcon;
        row.ColorKey = Colors.FirstOrDefault(c => c.IsSelected)?.Key ?? CustomTrackerVisuals.DefaultColorKey;
        row.Shape = IsAmount ? CustomShape.Amount : CustomShape.Tick;
        // A unit is meaningless on a Tick, and a stale one would resurface as "· min" on
        // a card that records no number if the shape ever changed back.
        row.Unit = IsAmount ? (Unit ?? string.Empty).Trim() : string.Empty;
        row.IncludeInReport = InReport;
        row.Kind = cadence?.Kind ?? TrackerKind.Daily;
        row.PerDayCount = cadence?.Kind == TrackerKind.PerDay ? cadence.PerDayCount : 0;

        await _custom.SaveAsync(row);

        NameError = string.Empty;
        IsPresented = false;
        Changed?.Invoke();
    }

    /// <summary>Retire the tracker: it stops being asked for and leaves the "+" sheet,
    /// and every entry it collected stays exactly where it is. This is deliberately not
    /// a delete — see <see cref="CustomTracker.IsArchived"/>.</summary>
    private async Task RetireAsync()
    {
        if (_editing == null)
        {
            IsPresented = false;
            return;
        }

        if (ConfirmRetire is not null && !await ConfirmRetire())
            return;

        await _custom.SetArchivedAsync(_editing.Id, true);
        IsPresented = false;
        Changed?.Invoke();
    }
}
