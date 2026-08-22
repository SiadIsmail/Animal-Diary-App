namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One water word-tile: the word plus how full its glass indicator is. The
/// owner sees the word and the glass; the number is never shown. Mirrors
/// <see cref="AppetiteOption"/>.</summary>
public class WaterOption : BaseViewModel
{
    public required int Level { get; init; }

    /// <summary>The localized level word, resolved LIVE on every read, never cached.
    /// The sheet VM is a singleton, so a cached word would freeze in the language
    /// active when it was constructed (e.g. German tiles on the English app). Re-raised
    /// via <see cref="RefreshWord"/> on a live language switch (see coding-standards).</summary>
    public string Word => ((WaterLevel)Level).GetDisplayName();

    /// <summary>Height (px) of the glass's filled portion: a visual level, no digits.</summary>
    public required double FillHeight { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-raise <see cref="Word"/> after a language switch.</summary>
    public void RefreshWord() => OnPropertyChanged(nameof(Word));
}

/// <summary>
/// Backs the Journal's water sheet: the shared <c>FelovaBottomSheet</c>
/// with TWO modes the owner flips between with an "Exact measurement" toggle at the top:
/// <list type="bullet">
/// <item><b>off (default)</b>: five relative word-tiles (Barely … A lot) with a
///   progressively-filled glass. This is ONE reading per day: saving replaces the
///   day's <see cref="WaterLevelEntry"/> (undo restores the previous, or removes it).</item>
/// <item><b>on</b>: the tiles are replaced by a single millilitre entry field, for
///   owners who measure the bowl. This is ADDITIVE: each save inserts a new
///   <see cref="WaterAmountEntry"/> event (undo removes just that one), so four
///   100 ml logs and one 400 ml log are the owner's choice: the report sums the day.</item>
/// </list>
/// The two coexist: a day can hold one relative reading and any number of exact ones.
/// Nothing here is judged: a low reading is a neutral fact.
/// </summary>
public class WaterSheetViewModel : BaseViewModel
{
    private const double GlassMaxFill = 22; // px at "A lot"

    private readonly WaterEntryService _service;
    private readonly DisplayUnitService _units;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    // Remembers the mode the owner last logged in, so the sheet reopens the way they
    // left it (a measurer keeps measuring; a quick-tapper keeps tapping). Session-
    // level: a singleton VM, so it survives tab switches and re-opens.
    private bool _lastExactMode;

    public WaterSheetViewModel(WaterEntryService service, DisplayUnitService units)
    {
        _service = service;
        _units = units;

        // ml, fl oz and cups. Cups are safe HERE and only here: a cup is a volume, and
        // water's density is the one that is constant enough for a fixed factor. The
        // same chip on APPETITE would be wrong for most foods (see AppetiteSheetViewModel).
        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.Volume))
            Units.Add(new UnitOption(unit));

        for (int level = 1; level <= 5; level++)
        {
            Options.Add(new WaterOption
            {
                Level = level,
                FillHeight = level / 5.0 * GlassMaxFill
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

        SelectCommand = new Command<WaterOption>(OnSelect);
        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
        ToggleExactCommand = new Command(() => ExactMode = !ExactMode);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    public ObservableCollection<WaterOption> Options { get; } = new();

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_WaterTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_WaterSub", _date);

    // ── Exact-measurement toggle ─────────────────────────────────────────────────
    // Off → the five relative tiles. On → the millilitre entry field. Flipping it
    // swaps which body shows; the two visibility flags drive the XAML.
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

    /// <summary>The millilitre entry field visible: toggle on.</summary>
    public bool ShowExact => ExactMode;

    private int _selectedLevel;
    public int SelectedLevel
    {
        get => _selectedLevel;
        private set => SetProperty(ref _selectedLevel, value);
    }

    private string _amountText = string.Empty;
    public string AmountText { get => _amountText; set => SetProperty(ref _amountText, value); }

    // ── The unit the measured amount is typed in ──

    /// <summary>ml · fl oz · cups, in catalog order.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.Volume);

    /// <summary>"How much · fl oz": the caption carries the unit, so the field is never
    /// a bare number.</summary>
    public string AmountLabel =>
        LocalizationManager.Instance.Format("Journal_WaterAmountLabelUnit", _unit.Label);

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

        // Reopen in the mode the owner last logged in. The exact field always starts
        // blank (it's additive: opening it is "add a measurement", never "edit the
        // last one"); the relative tiles pre-select the day's reading if there is one.
        ExactMode = _lastExactMode;
        AmountText = string.Empty;
        // The measured store is additive, so opening it means "add another": the unit
        // comes from what this owner's own entries resolved to, not from any one row.
        ApplyUnit(await _units.ResolveAsync(petId, UnitFamily.Volume));
        var todayLevel = (await _service.GetLevelsForDateAsync(petId, _date)).LastOrDefault();
        SetSelected(todayLevel?.Level ?? 0);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id.
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(AmountLabel));
    }

    /// <summary>Switching unit re-expresses what is already typed (240 ml becomes
    /// 8.1 fl oz) rather than relabelling it.</summary>
    private void OnSelectUnit(UnitOption? option)
    {
        if (option is null || option.Unit.Id == _unit.Id)
            return;

        var canonical = InputParser.TryParsePositive(AmountText, out var typed)
            ? UnitCatalog.ToCanonicalValue(typed, _unit)
            : (decimal?)null;

        ApplyUnit(option.Unit);

        if (canonical is decimal value)
            AmountText = UnitCatalog.Format(value, option.Unit);
    }

    private void OnSelect(WaterOption? option)
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
            LocalizationManager.Instance.Format("Journal_ToastWater", readout),
            undo));
    }

    // Exact mode: additive. Each save inserts a new event; undo removes just that one.
    private async Task<(Func<Task>? Undo, string Readout)> SaveAmountAsync()
    {
        if (!InputParser.TryParsePositive(AmountText, out var typed) || typed <= 0)
            return (null, string.Empty);

        var id = await _service.InsertAmountAsync(new WaterAmountEntry
        {
            PetId = _petId,
            Date = _date,
            Time = DateTime.Now.TimeOfDay,
            // Stored canonical (ml) at full precision; the unit column is provenance.
            AmountMl = UnitCatalog.ToCanonicalValue(typed, _unit),
            Unit = _unit.Id,
        });
        await _units.RememberAsync(_unit);

        // The toast quotes it back in the unit they just typed, which is the one moment
        // where the entry's own unit outranks the majority: they are looking at what
        // they wrote a second ago.
        var readout = UnitText.WithUnit(UnitCatalog.ToCanonicalValue(typed, _unit), _unit);
        return (() => _service.DeleteAmountAsync(id), readout);
    }

    // Relative mode: one reading per day (like Appetite). Replace the day's row if
    // there is one, otherwise insert. Undo restores the previous, or removes the row.
    private async Task<(Func<Task>? Undo, string Readout)> SaveLevelAsync()
    {
        if (SelectedLevel <= 0)
            return (null, string.Empty);

        var existing = (await _service.GetLevelsForDateAsync(_petId, _date)).FirstOrDefault();
        Func<Task> undo;
        if (existing != null)
        {
            var prevLevel = existing.Level;
            var prevTime = existing.Time;
            existing.Level = SelectedLevel;
            existing.Time = DateTime.Now.TimeOfDay;
            await _service.UpdateLevelAsync(existing);
            undo = () =>
            {
                existing.Level = prevLevel;
                existing.Time = prevTime;
                return _service.UpdateLevelAsync(existing);
            };
        }
        else
        {
            var id = await _service.InsertLevelAsync(new WaterLevelEntry
            {
                PetId = _petId,
                Date = _date,
                Time = DateTime.Now.TimeOfDay,
                Level = SelectedLevel
            });
            undo = () => _service.DeleteLevelAsync(id);
        }

        var readout = ((WaterLevel)SelectedLevel).GetDisplayName().ToLowerInvariant();
        return (undo, readout);
    }
}
