namespace Animal_Diary_App.Data.Models;

/// <summary>
/// The list of <see cref="Condition"/>s a pet can have, and the metadata (localized
/// name + icon) for each. This is what the condition picker, the Manage page and the
/// vet report read.
///
/// The trackers a condition introduces live in <see cref="CarePlanCatalog"/> (the
/// current care-plan model); this catalog is now purely the condition list.
/// </summary>
public static class ConditionCatalog
{
    /// <summary>Conditions shown in the picker, in display order. The empty id is
    /// the gentle "None / Not sure" default.</summary>
    public static IReadOnlyList<Condition> Conditions { get; } = new List<Condition>
    {
        new("",         "Condition_None",     "🌿"),
        new("diabetes", "Condition_Diabetes", "🩸"),
        new("ckd",      "Condition_Ckd",      "💧"),
        new("epilepsy", "Condition_Epilepsy", "⚡"),
    };

    /// <summary>Condition metadata for an id, falling back to "None / Not sure".</summary>
    public static Condition GetCondition(string? conditionId) =>
        Conditions.FirstOrDefault(c => c.Id == (conditionId ?? string.Empty)) ?? Conditions[0];
}
