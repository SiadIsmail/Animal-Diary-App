namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The Journal's single seam onto a pet's care plan. The plan is now PERSISTED: a
/// pet's <see cref="Tracker"/> rows are the source of truth, seeded once from
/// <see cref="CarePlanCatalog"/> (its conditions' defaults) via
/// <see cref="TrackerService"/>. The Journal only ever asks "what's this pet's
/// plan?": it never sees a condition name.
/// </summary>
public class CarePlanService
{
    private readonly TrackerService _trackers;
    private readonly PetConditionService _conditions;
    private readonly CustomTrackerService _custom;

    public CarePlanService(
        TrackerService trackers,
        PetConditionService conditions,
        CustomTrackerService custom)
    {
        _trackers = trackers;
        _conditions = conditions;
        _custom = custom;
    }

    /// <summary>The trackers the Journal should ask about for this pet. Reads the
    /// persisted rows, seeding them from the pet's conditions the first time. An
    /// unsaved pet (Id == 0) falls back to a derived, unpersisted plan.
    ///
    /// <para>Returns <see cref="CarePlanItem"/> rather than <see cref="Tracker"/>
    /// because the plan has two sources: the shipped trackers and the ones the owner
    /// made up, and this seam is where they become one list. Callers stay naive about
    /// which produced a line, exactly as they already are about seeding and the
    /// condition merge.</para></summary>
    public async Task<IReadOnlyList<CarePlanItem>> GetPlanAsync(Pet? pet)
    {
        if (pet == null || pet.Id == 0)
            return Project(CarePlanCatalog.BuildDefaultPlan(pet?.ConditionId)).ToList();

        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        var shipped = await _trackers.EnsureSeededAsync(pet.Id, conditionIds);

        // The owner's own trackers, after the shipped ones: care-plan order is the order
        // the Journal asks in, and the things every pet has come first. Archived ones are
        // absent by construction: retiring one is exactly "stop asking me about this".
        var own = await _custom.GetForPetAsync(pet.Id);

        return Project(shipped).Concat(own.Select(c => c.ToCarePlanItem())).ToList();
    }

    private static IEnumerable<CarePlanItem> Project(IEnumerable<Tracker> trackers) =>
        trackers.Select(CarePlanItem.FromTracker);
}
