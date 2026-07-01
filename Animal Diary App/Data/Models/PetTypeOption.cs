namespace Animal_Diary_App.Data.Models;

public class PetTypeOption
{
    /// <summary>Canonical (English) key used for storage and logic, e.g. "Dog", "Other".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Localized name shown in the type picker.</summary>
    public string DisplayName => Animal_Diary_App.Helpers.PetTypeNames.Localize(Name);

    public string Emoji { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}
