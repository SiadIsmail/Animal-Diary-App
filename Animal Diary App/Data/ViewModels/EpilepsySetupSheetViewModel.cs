namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The reusable Epilepsy setup sheet. Nothing to schedule — a seizure can't be
/// planned — so it explains (briefly) that it adds a seizure LOG that waits under
/// "+" in the Journal, then writes the pet's Seizure <see cref="Tracker"/> as an
/// Event. Concise and action-oriented, not a lesson on the condition.
/// </summary>
public class EpilepsySetupSheetViewModel : ConditionSetupSheetViewModel
{
    public EpilepsySetupSheetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers)
        : base(activePet, conditions, trackers)
    {
    }

    public override string ConditionId => "epilepsy";

    // Doubles as the Seizure tracker's editor. It has no controls — its whole job is to
    // say why seizures are written down as they happen rather than scheduled — which is
    // exactly the right answer when someone taps a Seizure row expecting a frequency
    // picker. Opened that way it neither claims epilepsy nor offers a cadence to break.
    public override string TitleText => LocalizationManager.Instance.GetString(
        LinkCondition ? "CondSetup_EpilepsyTitle" : "CondSetup_SeizureTitle");
    public override string SubtitleText => LocalizationManager.Instance.GetString(
        LinkCondition ? "CondSetup_EpilepsySub" : "CondSetup_SeizureSub");

    /// <summary>The one short explanation of WHY this is a log, not a daily task.</summary>
    public string BodyText => LocalizationManager.Instance.GetString("CondSetup_EpilepsyBody");

    protected override Task PersistAsync(int petId) =>
        Trackers.UpsertAsync(petId, TrackerId.Seizure, (t, isNew) =>
        {
            t.Kind = TrackerKind.Event;
            if (isNew)
                t.FromCondition = "epilepsy";
        });
}
