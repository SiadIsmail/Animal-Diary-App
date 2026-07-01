namespace Animal_Diary_App.Helpers;

/// <summary>
/// Maps the canonical (English) pet-type keys used in storage and logic
/// ("Dog", "Cat", …, "Other") to their localized display names. Custom,
/// user-entered types are returned unchanged since they can't be translated.
///
/// Keeping the stored value language-neutral means the "Other" branch and any
/// other type comparisons stay stable regardless of the active language.
/// </summary>
public static class PetTypeNames
{
    public static string Localize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        var key = type.Trim().ToLowerInvariant() switch
        {
            "dog" => "PetType_Dog",
            "cat" => "PetType_Cat",
            "bird" => "PetType_Bird",
            "rabbit" => "PetType_Rabbit",
            "fish" => "PetType_Fish",
            "other" => "PetType_Other",
            _ => null
        };

        return key == null ? type : LocalizationManager.Instance.GetString(key);
    }
}
