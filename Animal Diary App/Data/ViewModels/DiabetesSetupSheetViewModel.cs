namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The reusable Diabetes setup sheet. Answers one question: how often does the vet
/// want glucose checked: plus an optional target range. Saving writes the pet's
/// Glucose <see cref="Tracker"/>; the word "Diabetes" never reaches the Journal.
///
/// <para><b>The band is entered in the owner's own unit and stored with it.</b> A vet
/// quotes a range in whichever unit that practice uses, so asking for it in a unit the
/// owner has to convert first is asking them to make an arithmetic mistake about their
/// animal. <c>Tracker.Unit</c> records which one they used, and every surface that shows
/// the band converts it into the unit the readings beside it are in
/// (<c>Helpers.UnitText.Band</c>).</para>
/// </summary>
public class DiabetesSetupSheetViewModel : ConditionSetupSheetViewModel
{
    private readonly DisplayUnitService _units;

    // "1" | "2" | "3" | "asneeded"
    private string _freq = "3";

    public DiabetesSetupSheetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers,
        DisplayUnitService units)
        : base(activePet, conditions, trackers)
    {
        _units = units;

        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.Glucose))
            Units.Add(new UnitOption(unit));

        PickFrequencyCommand = new Command<string>(PickFrequency);
        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
    }

    public override string ConditionId => "diabetes";

    // Two doors, two framings. Opened from the condition it reads as setting up
    // diabetes; opened from the Glucose care-plan row it is simply the glucose editor,
    // because a target range belongs to the reading and not to a diagnosis.
    public override string TitleText => LocalizationManager.Instance.GetString(
        LinkCondition ? "CondSetup_DiabetesTitle" : "CondSetup_GlucoseTitle");
    public override string SubtitleText => LocalizationManager.Instance.GetString(
        LinkCondition ? "CondSetup_DiabetesSub" : "CondSetup_GlucoseSub");

    public ICommand PickFrequencyCommand { get; }
    public ICommand SelectUnitCommand { get; }

    // ── The unit the band is entered in ──

    /// <summary>mmol/L · mg/dL, in catalog order. Built once: the set never changes.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.Glucose);

    /// <summary>The caption carries the unit, so neither bound is ever a bare figure.</summary>
    public string RangeLabel =>
        LocalizationManager.Instance.Format("CondSetup_TargetRangeLabelUnit", _unit.Label);

    // ── Frequency segment (one bool per option for the DataTrigger highlight) ──
    public bool IsFreqOnce => _freq == "1";
    public bool IsFreqTwice => _freq == "2";
    public bool IsFreqThrice => _freq == "3";
    public bool IsFreqAsNeeded => _freq == "asneeded";

    // ── Optional target range ──
    private string _loText = string.Empty;
    public string LoText
    {
        get => _loText;
        set { if (SetProperty(ref _loText, value)) ClearRangeError(); }
    }

    private string _hiText = string.Empty;
    public string HiText
    {
        get => _hiText;
        set { if (SetProperty(ref _hiText, value)) ClearRangeError(); }
    }

    private string _rangeError = string.Empty;
    public string RangeError
    {
        get => _rangeError;
        private set { if (SetProperty(ref _rangeError, value)) OnPropertyChanged(nameof(HasRangeError)); }
    }
    public bool HasRangeError => !string.IsNullOrEmpty(RangeError);

    private void ClearRangeError() => RangeError = string.Empty;

    private void PickFrequency(string? value)
    {
        _freq = value switch { "1" or "2" or "3" or "asneeded" => value, _ => _freq };
        OnPropertyChanged(nameof(IsFreqOnce));
        OnPropertyChanged(nameof(IsFreqTwice));
        OnPropertyChanged(nameof(IsFreqThrice));
        OnPropertyChanged(nameof(IsFreqAsNeeded));
    }

    protected override async Task LoadAsync(int petId)
    {
        var glucose = await Trackers.GetByTrackerIdAsync(petId, TrackerId.Glucose);
        if (glucose == null)
        {
            PickFrequency("3");
            // No band yet, so nothing to reopen in: start in the unit this owner's own
            // readings resolve to, which is the one their vet most likely quoted.
            ApplyUnit(await _units.ResolveAsync(petId, UnitFamily.Glucose));
            LoText = HiText = string.Empty;
            return;
        }

        PickFrequency(glucose.Kind == TrackerKind.AsNeeded
            ? "asneeded"
            : glucose.PerDayCount.ToString());

        // An existing band reopens in the unit it was ENTERED in, exactly as an entry
        // does: reopening 4-8 mmol/L as though it were mg/dL and saving would write a
        // band eighteen times too low, from a screen that looked untouched.
        var unit = glucose.TargetRange is not null
            ? glucose.TargetUnit ?? UnitCatalog.Canonical(UnitFamily.Glucose)
            : await _units.ResolveAsync(petId, UnitFamily.Glucose);
        ApplyUnit(unit);

        LoText = glucose.TargetLo is decimal lo ? UnitCatalog.Format(lo, unit) : string.Empty;
        HiText = glucose.TargetHi is decimal hi ? UnitCatalog.Format(hi, unit) : string.Empty;
        ClearRangeError();
    }

    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id.
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(RangeLabel));
    }

    /// <summary>Switching unit re-expresses the bounds already typed rather than
    /// reinterpreting them: 4-8 becomes 72-144, never "4-8 mg/dL".</summary>
    private void OnSelectUnit(UnitOption? option)
    {
        if (option is null || option.Unit.Id == _unit.Id)
            return;

        var from = _unit;
        var lo = InputParser.TryParsePositive(LoText, out var loVal) ? (decimal?)loVal : null;
        var hi = InputParser.TryParsePositive(HiText, out var hiVal) ? (decimal?)hiVal : null;

        ApplyUnit(option.Unit);

        if (lo is decimal l)
            LoText = UnitCatalog.Format(
                UnitCatalog.ToCanonicalValue(l, from), option.Unit);
        if (hi is decimal h)
            HiText = UnitCatalog.Format(
                UnitCatalog.ToCanonicalValue(h, from), option.Unit);
    }

    protected override bool Validate()
    {
        if (!TryReadRange(out _, out _))
        {
            RangeError = LocalizationManager.Instance.GetString("CondSetup_RangeError");
            return false;
        }
        ClearRangeError();
        return true;
    }

    protected override async Task PersistAsync(int petId)
    {
        TryReadRange(out var lo, out var hi); // valid by now (Validate passed)

        await Trackers.UpsertAsync(petId, TrackerId.Glucose, (t, isNew) =>
        {
            if (_freq == "asneeded")
            {
                t.Kind = TrackerKind.AsNeeded;
                t.PerDayCount = 0;
            }
            else
            {
                t.Kind = TrackerKind.PerDay;
                t.PerDayCount = int.Parse(_freq);
            }

            // The band is stored AS ENTERED, in the unit recorded beside it: it is a pair
            // the owner typed once, quoting their vet, never summed or charted, so there
            // is nothing for a canonical form to make comparable. What matters is that
            // its source unit is recorded, which is this column (see Tracker.Unit).
            t.Unit = _unit.Id;
            t.TargetLo = lo;
            t.TargetHi = hi;
            if (isNew)
                t.FromCondition = "diabetes";
        });
    }

    // A range is valid when both bounds are empty (no range) OR both are present with
    // lo &lt; hi. A half-filled or inverted range is rejected so the value stays honest.
    private bool TryReadRange(out decimal? lo, out decimal? hi)
    {
        lo = hi = null;
        var hasLo = InputParser.TryParsePositive(LoText, out var loVal);
        var hasHi = InputParser.TryParsePositive(HiText, out var hiVal);

        if (!hasLo && !hasHi)
            return true;
        if (hasLo && hasHi && loVal < hiVal)
        {
            lo = loVal;
            hi = hiVal;
            return true;
        }
        return false;
    }
}
