namespace Animal_Diary_App.Data.Models;

/// <summary>
/// Which conditions have a reusable setup sheet (the "menu" behind both doors —
/// onboarding and Manage Pet). Conditions without one are simply added with the
/// trackers <see cref="CarePlanCatalog.ForCondition"/> gives them; conditions with
/// one open that sheet so the owner can tune it.
///
/// Kept as a tiny single source of truth so the picker, the add-condition flow and
/// the care-plan row routing all agree on which conditions are configurable.
/// </summary>
public static class ConditionSetup
{
    private static readonly HashSet<string> WithSheet = new(System.StringComparer.Ordinal)
    {
        "diabetes",
        "ckd",
        "epilepsy",
    };

    /// <summary>True when this condition has a setup sheet to open.</summary>
    public static bool HasSheet(string? conditionId) =>
        !string.IsNullOrEmpty(conditionId) && WithSheet.Contains(conditionId);
}
