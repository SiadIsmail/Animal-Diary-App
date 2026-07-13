namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The reusable CKD (kidney) setup sheet. Configures the weigh-in cadence and offers
/// Appetite / Water as daily trackers. Saving updates the pet's Weight tracker and
/// adds/removes Appetite + Water; the Journal only ever sees the resulting trackers.
/// </summary>
public class CkdSetupSheetViewModel : ConditionSetupSheetViewModel
{
    // TrackerKind for the weigh-in: Daily | TwiceWeekly | Weekly
    private TrackerKind _weightKind = TrackerKind.Weekly;

    public CkdSetupSheetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers)
        : base(activePet, conditions, trackers)
    {
        PickWeightFrequencyCommand = new Command<string>(PickWeightFrequency);
    }

    public override string ConditionId => "ckd";
    public override string TitleText => LocalizationManager.Instance.GetString("CondSetup_CkdTitle");
    public override string SubtitleText => LocalizationManager.Instance.GetString("CondSetup_CkdSub");

    public ICommand PickWeightFrequencyCommand { get; }

    // ── Weigh-in cadence (one bool per option for the DataTrigger highlight) ──
    public bool IsWeightDaily => _weightKind == TrackerKind.Daily;
    public bool IsWeightTwiceWeekly => _weightKind == TrackerKind.TwiceWeekly;
    public bool IsWeightWeekly => _weightKind == TrackerKind.Weekly;

    private bool _isAppetiteOn = true;
    public bool IsAppetiteOn
    {
        get => _isAppetiteOn;
        set => SetProperty(ref _isAppetiteOn, value);
    }

    private bool _isWaterOn = true;
    public bool IsWaterOn
    {
        get => _isWaterOn;
        set => SetProperty(ref _isWaterOn, value);
    }

    private void PickWeightFrequency(string? value)
    {
        _weightKind = value switch
        {
            "daily" => TrackerKind.Daily,
            "twiceweekly" => TrackerKind.TwiceWeekly,
            "weekly" => TrackerKind.Weekly,
            _ => _weightKind
        };
        OnPropertyChanged(nameof(IsWeightDaily));
        OnPropertyChanged(nameof(IsWeightTwiceWeekly));
        OnPropertyChanged(nameof(IsWeightWeekly));
    }

    protected override async Task LoadAsync(int petId)
    {
        var weight = await Trackers.GetByTrackerIdAsync(petId, TrackerId.Weight);
        _weightKind = weight?.Kind is TrackerKind.Daily or TrackerKind.TwiceWeekly or TrackerKind.Weekly
            ? weight.Kind
            : TrackerKind.Weekly;
        OnPropertyChanged(nameof(IsWeightDaily));
        OnPropertyChanged(nameof(IsWeightTwiceWeekly));
        OnPropertyChanged(nameof(IsWeightWeekly));

        // A fresh setup (neither extra exists yet) defaults both on; once CKD has
        // written at least one, a re-open reflects the real on/off state.
        var appetite = await Trackers.GetByTrackerIdAsync(petId, TrackerId.Appetite);
        var water = await Trackers.GetByTrackerIdAsync(petId, TrackerId.Water);
        var fresh = appetite == null && water == null;
        IsAppetiteOn = fresh || appetite != null;
        IsWaterOn = fresh || water != null;
    }

    protected override async Task PersistAsync(int petId)
    {
        // Weight is an always-on default; CKD only tunes its cadence (never claims it,
        // so removing CKD later never removes the weigh-in).
        await Trackers.UpsertAsync(petId, TrackerId.Weight, t => t.Kind = _weightKind);

        if (IsAppetiteOn)
            await Trackers.UpsertAsync(petId, TrackerId.Appetite, t =>
            {
                t.Kind = TrackerKind.Daily;
                t.FromCondition ??= "ckd";
            });
        else
            await Trackers.RemoveByTrackerIdAsync(petId, TrackerId.Appetite);

        if (IsWaterOn)
            await Trackers.UpsertAsync(petId, TrackerId.Water, t =>
            {
                t.Kind = TrackerKind.Daily;
                t.FromCondition ??= "ckd";
            });
        else
            await Trackers.RemoveByTrackerIdAsync(petId, TrackerId.Water);
    }
}
