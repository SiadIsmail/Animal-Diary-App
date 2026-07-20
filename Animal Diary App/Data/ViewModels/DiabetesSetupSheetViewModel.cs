namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;

/// <summary>
/// The reusable Diabetes setup sheet. Answers one question — how often does the vet
/// want glucose checked — plus an optional target range. Saving writes the pet's
/// Glucose <see cref="Tracker"/>; the word "Diabetes" never reaches the Journal.
/// </summary>
public class DiabetesSetupSheetViewModel : ConditionSetupSheetViewModel
{
    // "1" | "2" | "3" | "asneeded"
    private string _freq = "3";

    public DiabetesSetupSheetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers)
        : base(activePet, conditions, trackers)
    {
        PickFrequencyCommand = new Command<string>(PickFrequency);
    }

    public override string ConditionId => "diabetes";
    public override string TitleText => LocalizationManager.Instance.GetString("CondSetup_DiabetesTitle");
    public override string SubtitleText => LocalizationManager.Instance.GetString("CondSetup_DiabetesSub");

    public ICommand PickFrequencyCommand { get; }

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
            LoText = HiText = string.Empty;
            return;
        }

        PickFrequency(glucose.Kind == TrackerKind.AsNeeded
            ? "asneeded"
            : glucose.PerDayCount.ToString());

        LoText = glucose.TargetLo?.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        HiText = glucose.TargetHi?.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        ClearRangeError();
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

        await Trackers.UpsertAsync(petId, TrackerId.Glucose, t =>
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

            t.Unit = "mmol/L";
            t.TargetLo = lo;
            t.TargetHi = hi;
            t.FromCondition ??= "diabetes";
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
