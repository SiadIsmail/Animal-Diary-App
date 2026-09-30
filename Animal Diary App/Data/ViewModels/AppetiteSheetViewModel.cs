namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One appetite word-tile: the word plus how full its bowl indicator is.
/// The person sees the word and the bowl; the number is never shown.</summary>
public class AppetiteOption : BaseViewModel
{
    public required int Level { get; init; }

    /// <summary>The localized level word, resolved LIVE on every read, never cached.
    /// The sheet VM is a singleton, so a cached word would freeze in the language
    /// active at construction. Re-raised via <see cref="RefreshWord"/> on a live
    /// language switch (see coding-standards).</summary>
    public string Word => ((AppetiteLevel)Level).GetDisplayName();

    /// <summary>Height (px) of the bowl's filled portion: a visual level, no digits.</summary>
    public required double FillHeight { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-raise <see cref="Word"/> after a language switch.</summary>
    public void RefreshWord() => OnPropertyChanged(nameof(Word));
}

/// <summary>
/// Backs the Journal's appetite sheet: the shared <c>FelovaBottomSheet</c>
/// with TWO modes flipped by an "Exact measurement" toggle (mirrors the water sheet):
/// <list type="bullet">
/// <item><b>off (default)</b>: five word-tiles (Didn't eat … Everything) with a
///   filling bowl. ONE reading per day: saving replaces the day's
///   <see cref="AppetiteEntry"/> (undo restores the previous, or removes it).</item>
/// <item><b>on</b>: the tiles are replaced by a grams entry field. ADDITIVE: each
///   save inserts a new <see cref="AppetiteAmountEntry"/> event (undo removes just it),
///   so several meals sum to the day's total in the report.</item>
/// </list>
/// Both modes carry an optional free-text <c>Food</c> label, pre-filled with the pet's
/// last-entered food and freely editable. No "3/5" anywhere; nothing is judged.
/// </summary>
public class AppetiteSheetViewModel : BaseViewModel
{
    private const double BowlMaxFill = 22; // px at "Everything"

    private readonly AppetiteEntryService _service;
    private readonly DisplayUnitService _units;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    // Remembers the mode the owner last logged in, so the sheet reopens the way they
    // left it. Session-level (a singleton VM).
    private bool _lastExactMode;

    public AppetiteSheetViewModel(AppetiteEntryService service, DisplayUnitService units)
    {
        _service = service;
        _units = units;

        // GRAMS AND OUNCES ONLY, and there will never be a third.
        //
        // Owners do measure dry food in cups, and adding a "cups" chip here is one of the
        // most reasonable-sounding requests this sheet will ever get. It is wrong: a cup
        // is a VOLUME and this is a MASS, so the conversion depends on the density of the
        // particular food, and any single factor picked here is wrong for most of them.
        // The app would be inventing a number and storing it as the owner's own reading.
        // Cups for WATER are fine (see WaterSheetViewModel): water's density is the one
        // that is safely constant.
        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.FoodMass))
            Units.Add(new UnitOption(unit));

        for (int level = 1; level <= 5; level++)
        {
            Options.Add(new AppetiteOption
            {
                Level = level,
                FillHeight = level / 5.0 * BowlMaxFill
            });
        }

        // Live language switch: re-resolve every tile's word (they're never cached).
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var o in Options)
                o.RefreshWord();
            foreach (var u in Units)
                u.RefreshLocalized();
            OnPropertyChanged(nameof(AmountLabel));
        };

        SelectCommand = new Command<AppetiteOption>(OnSelect);
        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
        ToggleExactCommand = new Command(() => ExactMode = !ExactMode);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    public ObservableCollection<AppetiteOption> Options { get; } = new();

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_AppetiteTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_AppetiteSub", _date);

    // ── Exact-measurement toggle ─────────────────────────────────────────────────
    private bool _exactMode;
    public bool ExactMode
    {
        get => _exactMode;
        set
        {
            if (SetProperty(ref _exactMode, value))
            {
                OnPropertyChanged(nameof(ShowLevels));
                OnPropertyChanged(nameof(ShowExact));
            }
        }
    }

    /// <summary>Relative tiles visible: the default mode (toggle off).</summary>
    public bool ShowLevels => !ExactMode;

    /// <summary>The grams entry field visible: toggle on.</summary>
    public bool ShowExact => ExactMode;

    private int _selectedLevel;
    public int SelectedLevel
    {
        get => _selectedLevel;
        private set => SetProperty(ref _selectedLevel, value);
    }

    private string _gramsText = string.Empty;
    public string GramsText { get => _gramsText; set => SetProperty(ref _gramsText, value); }

    /// <summary>Optional free-text food label, shared by both modes and pre-filled
    /// with the pet's last-entered food (editable / clearable).</summary>
    private string _food = string.Empty;
    public string Food { get => _food; set => SetProperty(ref _food, value); }

    // ── The unit the measured amount is typed in ──

    /// <summary>g · oz, in catalog order. No cups: see the constructor.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.FoodMass);

    /// <summary>"How much · oz": the caption carries the unit, so the field is never a
    /// bare number.</summary>
    public string AmountLabel =>
        LocalizationManager.Instance.Format("Journal_AppetiteGramsLabelUnit", _unit.Label);

    public ICommand SelectCommand { get; }
    public ICommand SelectUnitCommand { get; }
    public ICommand ToggleExactCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        // Reopen in the last-used mode; the grams field always starts blank (additive).
        // The tiles pre-select the day's reading; Food pre-fills from the last one the
        // owner named, so a steady diet is one confirm: still editable/clearable.
        ExactMode = _lastExactMode;
        GramsText = string.Empty;
        // Additive store, so opening means "add another": the unit comes from what this
        // owner's own entries resolved to, not from any one row.
        ApplyUnit(await _units.ResolveAsync(petId, UnitFamily.FoodMass));
        var today = await _service.GetForDateAsync(petId, _date);
        SetSelected(today.LastOrDefault()?.Level ?? 0);
        Food = await _service.GetLastFoodAsync(petId);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void OnSelect(AppetiteOption? option)
    {
        if (option != null)
            SetSelected(option.Level);
    }

    private void SetSelected(int level)
    {
        SelectedLevel = level;
        foreach (var o in Options)
            o.IsSelected = o.Level == level;
    }

    private async Task SaveAsync()
    {
        var (undo, readout) = ExactMode
            ? await SaveAmountAsync()
            : await SaveLevelAsync();
        if (undo == null)
            return; // nothing valid entered

        _lastExactMode = ExactMode; // reopen in the mode they just logged in
        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.Format("Journal_ToastAppetite", readout),
            undo));
    }

    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id.
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(AmountLabel));
    }

    /// <summary>Switching unit re-expresses what is already typed (85 g becomes 3 oz)
    /// rather than relabelling it.</summary>
    private void OnSelectUnit(UnitOption? option)
    {
        if (option is null || option.Unit.Id == _unit.Id)
            return;

        var canonical = InputParser.TryParsePositive(GramsText, out var typed)
            ? UnitCatalog.ToCanonicalValue(typed, _unit)
            : (decimal?)null;

        ApplyUnit(option.Unit);

        if (canonical is decimal value)
            GramsText = UnitCatalog.Format(value, option.Unit);
    }

    // Exact mode: additive. Each save inserts a new amount event; undo removes just it.
    private async Task<(Func<Task>? Undo, string Readout)> SaveAmountAsync()
    {
        if (!InputParser.TryParsePositive(GramsText, out var typed) || typed <= 0)
            return (null, string.Empty);

        var food = Food?.Trim() ?? string.Empty;
        var id = await _service.InsertAmountAsync(new AppetiteAmountEntry
        {
            PetId = _petId,
            Date = _date,
            Time = DateTime.Now.TimeOfDay,
            // Stored canonical (grams) at full precision; the unit column is provenance.
            Grams = UnitCatalog.ToCanonicalValue(typed, _unit),
            Unit = _unit.Id,
            Food = food
        });
        await _units.RememberAsync(_unit);

        // Quoted back in the unit they just typed: the one moment where the entry's own
        // unit outranks the majority, because they are looking at what they wrote.
        var readout = UnitText.WithUnit(UnitCatalog.ToCanonicalValue(typed, _unit), _unit);
        return (() => _service.DeleteAmountAsync(id), readout);
    }

    // Relative mode: one reading per day (like Mood / Weight). Replace the day's row if
    // there is one, otherwise insert. Undo restores the previous, or removes the row.
    private async Task<(Func<Task>? Undo, string Readout)> SaveLevelAsync()
    {
        if (SelectedLevel <= 0)
            return (null, string.Empty);

        var food = Food?.Trim() ?? string.Empty;
        var existing = (await _service.GetForDateAsync(_petId, _date)).FirstOrDefault();
        Func<Task> undo;
        if (existing != null)
        {
            var prevLevel = existing.Level;
            var prevFood = existing.Food;
            var prevTime = existing.Time;
            existing.Level = SelectedLevel;
            existing.Food = food;
            existing.Time = DateTime.Now.TimeOfDay;
            await _service.UpdateAsync(existing);
            undo = () =>
            {
                existing.Level = prevLevel;
                existing.Food = prevFood;
                existing.Time = prevTime;
                return _service.UpdateAsync(existing);
            };
        }
        else
        {
            var id = await _service.InsertAsync(new AppetiteEntry
            {
                PetId = _petId,
                Date = _date,
                Time = DateTime.Now.TimeOfDay,
                Level = SelectedLevel,
                Food = food
            });
            undo = () => _service.DeleteAsync(id);
        }

        var readout = ((AppetiteLevel)SelectedLevel).GetDisplayName().ToLowerInvariant();
        return (undo, readout);
    }
}
