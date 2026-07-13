namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The Journal's single seam onto a pet's care plan. The plan is now PERSISTED: a
/// pet's <see cref="Tracker"/> rows are the source of truth, seeded once from
/// <see cref="CarePlanCatalog"/> (its conditions' defaults) via
/// <see cref="TrackerService"/>. The Journal only ever asks "what's this pet's
/// plan?" — it never sees a condition name.
/// </summary>
public class CarePlanService
{
    private readonly TrackerService _trackers;
    private readonly PetConditionService _conditions;

    public CarePlanService(TrackerService trackers, PetConditionService conditions)
    {
        _trackers = trackers;
        _conditions = conditions;
    }

    /// <summary>The trackers the Journal should ask about for this pet. Reads the
    /// persisted rows, seeding them from the pet's conditions the first time. An
    /// unsaved pet (Id == 0) falls back to a derived, unpersisted plan.</summary>
    public async Task<IReadOnlyList<Tracker>> GetPlanAsync(Pet? pet)
    {
        if (pet == null || pet.Id == 0)
            return CarePlanCatalog.BuildDefaultPlan(pet?.ConditionId);

        var conditionIds = await _conditions.GetConditionIdsAsync(pet);
        return await _trackers.EnsureSeededAsync(pet.Id, conditionIds);
    }
}
