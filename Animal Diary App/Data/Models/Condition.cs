namespace Animal_Diary_App.Data.Models;

/// <summary>One ongoing condition a pet can have. Pure data — see
/// <see cref="ConditionCatalog"/> for the list and the items each maps to.
/// <paramref name="NameKey"/> is an AppStrings key; the display <see cref="Name"/>
/// is resolved from it so condition names follow the app language.</summary>
public record Condition(string Id, string NameKey, string Icon)
{
    /// <summary>Localized display name, resolved from <see cref="NameKey"/>.</summary>
    public string Name => Animal_Diary_App.Helpers.LocalizationManager.Instance.GetString(NameKey);
}
