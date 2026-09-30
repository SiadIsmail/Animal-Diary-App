namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// One row of the sheet's unit picker: a unit, its label, and whether it is the one
/// currently selected. A tiny VM rather than a raw <see cref="UnitDef"/> so the chips
/// can bind their selected state (a record struct cannot raise change notifications).
/// </summary>
public class UnitOption : BaseViewModel
{
    public UnitOption(UnitDef unit) => Unit = unit;

    public UnitDef Unit { get; }

    /// <summary>The unit's own label, resolved per read so a live language switch
    /// relabels an open sheet.</summary>
    public string Label => Unit.Label;

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-raise the label after a language switch. The getter does the work.</summary>
    public void RefreshLocalized() => OnPropertyChanged(nameof(Label));
}

/// <summary>
/// Backs the Journal's weight sheet: a value stepper plus a unit picker, in the shared
/// <c>FelovaBottomSheet</c>. Saves to the day's <see cref="PetEntry"/>; Save's undo
/// restores the previous weight <b>and</b> the previous unit.
///
/// <para><b>The weight is stored in kilograms whatever the owner picked</b>, and
/// <c>PetEntry.WeightUnit</c> records what they typed. Two things follow, and both are
/// load-bearing:</para>
/// <list type="bullet">
/// <item><b>Editing an existing entry opens in that entry's OWN unit</b>, not the
/// remembered one. Otherwise reopening an old kilogram weigh-in on a device that has
/// since switched to pounds silently relabels it, and the owner saves a number that now
/// means something else.</item>
/// <item><b>An edit that does not change the number does not rewrite it.</b> The stored
/// kilograms are re-derived only when the typed text or the chosen unit actually
/// changed; otherwise every open-and-save would run the value through a divide and a
/// multiply and let it drift a digit at a time.</item>
/// </list>
/// </summary>
public class WeightSheetViewModel : BaseViewModel
{
    private readonly PetEntryService _petEntries;
    private readonly DisplayUnitService _units;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    /// <summary>What the sheet opened showing. The save path compares against these to
    /// decide whether the owner actually touched the number: see the class remarks.</summary>
    private string _openedText = string.Empty;
    private string _openedUnitId = string.Empty;

    /// <summary>The kilograms already on the row when the sheet opened, kept so an
    /// untouched save can write them back byte-identical rather than re-deriving them.</summary>
    private decimal _openedCanonical;

    public WeightSheetViewModel(PetEntryService petEntries, DisplayUnitService units)
    {
        _petEntries = petEntries;
        _units = units;

        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.Weight))
            Units.Add(new UnitOption(unit));

        StepCommand = new Command<string>(OnStep);
        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_WeightTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_WeightSub", _date);

    private string _valueText = string.Empty;
    public string ValueText { get => _valueText; set => SetProperty(ref _valueText, value); }

    /// <summary>kg · lb · g, in catalog order. Built once: the set never changes.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.Weight);

    /// <summary>The unit the owner is typing in right now.</summary>
    public UnitDef SelectedUnit => _unit;

    /// <summary>The field's caption carries the unit, so the number above the keyboard
    /// is never a bare figure: "Weight · lb".</summary>
    public string FieldLabel =>
        LocalizationManager.Instance.Format("Journal_WeightFieldLabelUnit", _unit.Label);

    public ICommand StepCommand { get; }
    public ICommand SelectUnitCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        var existing = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
        var editing = existing is not null && existing.Weight > 0;

        // Editing reopens in the entry's own unit; a new weigh-in opens in whatever the
        // owner last picked (falling back to their history, then to the locale guess).
        var unit = editing
            ? UnitCatalog.Get(UnitFamily.Weight, existing!.WeightUnit)
            : await _units.ResolveAsync(petId, UnitFamily.Weight);

        decimal canonical;
        if (editing)
            canonical = existing!.Weight;
        else
        {
            var latest = await _petEntries.GetLatestWeightEntryAsync(petId);
            // 4 kg is the resting guess for a pet with no history at all: a cat. It is a
            // starting point for the stepper, never a value: nothing is saved until the
            // owner presses Save.
            canonical = latest?.Weight ?? 4.0m;
        }

        ApplyUnit(unit);
        ValueText = UnitCatalog.Format(canonical, unit);

        _openedText = ValueText;
        _openedUnitId = unit.Id;
        _openedCanonical = canonical;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    /// <summary>Point the sheet at a unit: the selection chips, the caption, and the
    /// stepper's increment all follow from it.</summary>
    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id rather than by
        // reference (AI/coding-standards.md).
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(SelectedUnit));
        OnPropertyChanged(nameof(FieldLabel));
    }

    /// <summary>
    /// Switching unit <b>re-expresses the number the owner is looking at</b>, it does
    /// not reinterpret it. Tapping "lb" while 5.2 kg is on screen shows 11.46, because
    /// the alternative is a sheet that silently decides the cat now weighs 5.2 lb.
    /// </summary>
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
        // The parameter is the DIRECTION (-1 / +1); how far one tap moves is the unit's
        // own business, so a gram stepper does not crawl in hundredths of a gram.
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var direction))
            return;

        var step = UnitCatalog.Step(_unit) * direction;
        var current = InputParser.TryParsePositive(ValueText, out var v) ? v : 0m;
        var next = Math.Max(0m, decimal.Round(current + step, _unit.Decimals, MidpointRounding.AwayFromZero));
        ValueText = UnitCatalog.Format(next, _unit);
    }

    private async Task SaveAsync()
    {
        if (!InputParser.TryParsePositive(ValueText, out var typed) || typed <= 0)
            return;

        // THE GUARD, decided in one pure place so it can be proven rather than trusted:
        // an open-and-save that changed nothing writes back exactly what was read.
        var canonical = UnitCatalog.CanonicalForSave(
            typed, _unit, ValueText, _openedCanonical, _openedText, _openedUnitId);

        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        var prevWeight = entry?.Weight ?? 0m;
        var prevUnit = entry?.WeightUnit;
        var prevTicks = entry?.WeightTimeTicks;

        await WriteWeightAsync(canonical, _unit.Id, DateTime.Now.TimeOfDay.Ticks);

        // Remembered on SAVE, never on merely tapping a chip: what the owner chose and
        // kept is the signal, and it is device-scoped, so the next pet opens on it too.
        await _units.RememberAsync(_unit);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Toast_WeightSaved1"),
            () => WriteWeightAsync(prevWeight, prevUnit, prevTicks)));
    }

    // Write just the weight columns of the day's entry, leaving mood untouched.
    private async Task WriteWeightAsync(decimal weight, string? unitId, long? timeTicks)
    {
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        if (entry != null)
        {
            entry.Weight = weight;
            entry.WeightUnit = unitId;
            entry.WeightTimeTicks = timeTicks;
            await _petEntries.UpdatePetEntryAsync(entry);
        }
        else
        {
            await _petEntries.SavePetEntryAsync(new PetEntry
            {
                PetId = _petId,
                Date = _date,
                Weight = weight,
                WeightUnit = unitId,
                WeightTimeTicks = timeTicks
            });
        }
    }
}
