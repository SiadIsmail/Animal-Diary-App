namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's glucose sheet (a <c>FelovaBottomSheet</c>
/// body). New functionality, so it lives in its own ViewModel rather than growing
/// the CalendarViewModel. Opening pre-fills the stepper from the pet's most recent
/// reading and pre-selects Before/After food by time of day; Save writes one
/// <see cref="GlucoseEntry"/> and raises <see cref="Saved"/> so the Journal can
/// toast + refresh.
///
/// The value itself is a precise readout: shown to its unit's own precision, never
/// rounded away.
///
/// <para><b>mmol/L or mg/dL, chosen per reading.</b> The value is stored in mmol/L
/// whatever the owner picks and <c>GlucoseEntry.Unit</c> records what they typed. This is
/// the highest-stakes pair in the app: the two differ by a factor of eighteen, so without
/// mg/dL the app is unusable for a diabetic pet in the US, and a number shown in the
/// wrong one is a different clinical picture rather than a rounding error.</para>
/// </summary>
public class GlucoseSheetViewModel : BaseViewModel
{
    private readonly GlucoseEntryService _service;
    private readonly DisplayUnitService _units;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public GlucoseSheetViewModel(GlucoseEntryService service, DisplayUnitService units)
    {
        _service = service;
        _units = units;

        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.Glucose))
            Units.Add(new UnitOption(unit));

        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
        StepCommand = new Command<string>(OnStep);
        PickContextCommand = new Command<string>(OnPickContext);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>Raised after a reading is saved, carrying the confirmation line and
    /// the undo (remove the just-saved reading) for the Journal's toast.</summary>
    public event Action<JournalSaveResult>? Saved;

    // ── Presentation state ─────────────────────────────────────────────────────
    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    public string Title => LocalizationManager.Instance.Format("Journal_GlucoseTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_GlucoseSub", _date);

    private string _valueText = string.Empty;
    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    private FoodContext _context = FoodContext.BeforeFood;
    public FoodContext Context
    {
        get => _context;
        private set
        {
            if (SetProperty(ref _context, value))
            {
                OnPropertyChanged(nameof(IsBeforeFood));
                OnPropertyChanged(nameof(IsAfterFood));
            }
        }
    }

    public bool IsBeforeFood => Context == FoodContext.BeforeFood;
    public bool IsAfterFood => Context == FoodContext.AfterFood;

    // ── Unit ───────────────────────────────────────────────────────────────────

    /// <summary>mmol/L · mg/dL, in catalog order. Built once: the set never changes.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.Glucose);

    /// <summary>The unit the owner is typing in right now.</summary>
    public UnitDef SelectedUnit => _unit;

    /// <summary>The field's caption carries the unit, so the number above the keyboard is
    /// never a bare figure: "Blood glucose · mg/dL".</summary>
    public string FieldLabel =>
        LocalizationManager.Instance.Format("Journal_GlucoseFieldLabelUnit", _unit.Label);

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand SelectUnitCommand { get; }
    public ICommand StepCommand { get; }
    public ICommand PickContextCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Prepare and present the sheet for a pet + date. Pre-fills the value
    /// from the last reading and pre-selects the food context by time of day.</summary>
    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        // Glucose is an EVENT store: opening always means "add another", never "edit the
        // last", so the sheet opens in the unit the owner's own readings resolved to
        // rather than in any one entry's unit.
        ApplyUnit(await _units.ResolveAsync(petId, UnitFamily.Glucose));

        var recent = await _service.GetMostRecentAsync(petId);
        // 7 mmol/L is the resting starting point for a pet with no readings at all: a
        // stepper origin, never a value. Nothing is saved until the owner presses Save.
        var start = recent?.Value ?? 7.0m;
        ValueText = UnitCatalog.Format(start, _unit);

        // Before food if early morning or late afternoon/evening, else after food,
        // the same gentle rule the prototype uses.
        var hour = DateTime.Now.Hour;
        Context = (hour < 11 || hour >= 16) ? FoodContext.BeforeFood : FoodContext.AfterFood;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    /// <summary>Point the sheet at a unit: the chips and the caption follow from it.</summary>
    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id (AI/coding-standards.md).
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(SelectedUnit));
        OnPropertyChanged(nameof(FieldLabel));
    }

    /// <summary>Switching unit <b>re-expresses the number on screen</b>, it does not
    /// reinterpret it: tapping mg/dL while 7.6 is showing gives 137. Reinterpreting would
    /// turn a normal reading into a fatal-looking one with a single tap.</summary>
    private void OnSelectUnit(UnitOption? option)
    {
        if (option is null || option.Unit.Id == _unit.Id)
            return;

        var canonical = InputParser.TryParsePositive(ValueText, out var typed)
            ? UnitCatalog.ToCanonicalValue(typed, _unit)
            : (decimal?)null;

        ApplyUnit(option.Unit);

        if (canonical is decimal value)
            ValueText = UnitCatalog.Format(value, option.Unit);
    }

    private void OnStep(string? raw)
    {
        // The DIRECTION (-1 / +1); how far one tap moves is the unit's own business, so
        // mg/dL steps by 1 rather than crawling in tenths of a milligram per decilitre.
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var direction))
            return;

        var step = UnitCatalog.Step(_unit) * direction;
        var current = InputParser.TryParsePositive(ValueText, out var v) ? v : 0m;
        var next = Math.Max(0m, decimal.Round(current + step, _unit.Decimals, MidpointRounding.AwayFromZero));
        ValueText = UnitCatalog.Format(next, _unit);
    }

    private void OnPickContext(string? which) =>
        Context = which == "after" ? FoodContext.AfterFood : FoodContext.BeforeFood;

    private async Task SaveAsync()
    {
        if (!InputParser.TryParsePositive(ValueText, out var value) || value <= 0)
            return;

        var id = await _service.InsertAsync(new GlucoseEntry
        {
            PetId = _petId,
            Date = _date,
            Time = DateTime.Now.TimeOfDay,
            // Stored canonical (mmol/L) at full precision; the unit column is provenance.
            Value = UnitCatalog.ToCanonicalValue(value, _unit),
            Unit = _unit.Id,
            Context = Context
        });

        // Remembered on SAVE, never on merely tapping a chip.
        await _units.RememberAsync(_unit);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Journal_ToastGlucose"),
            () => _service.DeleteAsync(id)));
    }
}
