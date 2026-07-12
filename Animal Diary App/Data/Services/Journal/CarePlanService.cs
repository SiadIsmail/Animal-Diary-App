namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The Journal's single seam onto a pet's care plan. Today the plan is derived
/// from the pet's condition via <see cref="CarePlanCatalog"/> (new pets: Mood +
/// Weight; conditions add the rest). It lives behind this service so the later pet
/// page can switch it to persisted, per-pet <see cref="Tracker"/> rows without the
/// Journal changing — the Journal only ever asks "what's this pet's plan?".
/// </summary>
public class CarePlanService
{
    /// <summary>The trackers the Journal should ask about for this pet.</summary>
    public IReadOnlyList<Tracker> GetPlan(Pet? pet) =>
        CarePlanCatalog.BuildDefaultPlan(pet?.ConditionId);
}
