namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>
/// Join row linking a pet to ONE ongoing condition. A pet can carry several
/// conditions at once (e.g. Diabetes + CKD), so conditions are stored as rows here
/// rather than a single column on <see cref="Pet"/>.
///
/// The legacy single <see cref="Pet.ConditionId"/> column is kept for backward
/// compatibility and migrated into a row on first read (see
/// <c>PetConditionService</c>), so existing pets keep their condition with no manual
/// migration step.
/// </summary>
public class PetCondition
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PetId { get; set; }

    /// <summary>The condition id (see <see cref="ConditionCatalog"/>), e.g. "diabetes".
    /// Empty ids are never stored.</summary>
    public string ConditionId { get; set; } = string.Empty;
}
