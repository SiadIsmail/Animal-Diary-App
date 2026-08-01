namespace Animal_Diary_App.Data.Models;

/// <summary>
/// How one tracker looks and reads, everywhere it appears.
///
/// <para><see cref="LabelKey"/> is a localization key, never a translated string —
/// this is a static table, and a singleton holding a resolved string would survive a
/// live language switch in the old language (see AI/coding-standards.md). Callers
/// resolve it per read.</para>
///
/// <para>The colours are token names from <c>Resources/Styles/Colors.xaml</c>,
/// resolved through <c>Helpers/AppColors</c> — not literals.</para>
/// </summary>
/// <param name="Icon">The emoji shown on chips, timeline tiles and care-plan rows.</param>
/// <param name="LabelKey">AppStrings key for the tracker's name.</param>
/// <param name="TintKey">Icon-tile background on the Journal timeline and chips.</param>
/// <param name="RowTintKey">Icon-tile background on the Manage page's care-plan rows.</param>
/// <param name="RowInkKey">Icon foreground on those rows.</param>
public readonly record struct TrackerVisual(
    string Icon,
    string LabelKey,
    string TintKey,
    string RowTintKey,
    string RowInkKey);

/// <summary>
/// <b>The one tracker → (icon, label, colour) table.</b> The Journal chips, the
/// Journal timeline, the "anything else" sheet, the Today next-up card and the Manage
/// page's care-plan rows all read it.
///
/// <para><b>Why:</b> the same five emoji and five label keys used to be written out in
/// four separate <c>switch</c> expressions across three files, so a new tracker looked
/// right in some surfaces and fell through to a mood face in others — and the Manage
/// page's copy used raw ARGB literals instead of the palette, which no theme change
/// could reach. Adding a tracker is now one line here.</para>
/// </summary>
public static class TrackerVisuals
{
    /// <summary>Anything unmapped: a neutral dot on the recessed-well colours, never a
    /// borrowed identity. A tracker missing from <see cref="Map"/> should look plainly
    /// unfinished rather than quietly impersonate mood, which is what the old
    /// <c>_ =&gt; mood</c> fallbacks did.</summary>
    public static readonly TrackerVisual Fallback =
        new("•", "Journal_MoodTitle", "Well", "Well", "Ink");

    private static readonly IReadOnlyDictionary<TrackerId, TrackerVisual> Map =
        new Dictionary<TrackerId, TrackerVisual>
        {
            [TrackerId.Glucose] = new("🩸", "Journal_GlucoseCheck", "RoseTint", "RoseTint", "RoseDeep"),
            [TrackerId.Mood] = new("🙂", "Journal_MoodTitle", "TealTint", "TealTint", "TealDeep"),
            [TrackerId.Appetite] = new("🍽️", "Journal_Appetite", "HoneyWarmTint", "HoneyWarmTint", "HoneyDeep"),
            [TrackerId.Weight] = new("⚖️", "Journal_WeighIn", "BlueTint", "BlueTint", "BlueInk"),
            [TrackerId.Water] = new("💧", "Journal_Water", "BlueTint", "BlueTint", "BlueInk"),
            // Seizure is the one tracker whose timeline tile and care-plan row use
            // different violets (VioletTint is darker than VioletSoftTint). That
            // predates this table and is preserved deliberately — unifying them is a
            // design decision, not a refactor.
            [TrackerId.Seizure] = new("⚡", "Journal_Seizure", "VioletTint", "VioletSoftTint", "Violet"),
        };

    /// <summary>Takes a nullable id because a <c>PendingItem</c> for a medication dose
    /// carries none — those never reach here today (both call sites gate on
    /// <c>PendingKind.Tracker</c>), and if one ever does it should read as unfinished
    /// rather than silently render as mood.</summary>
    public static TrackerVisual For(TrackerId? id) =>
        id is TrackerId t && Map.TryGetValue(t, out var v) ? v : Fallback;

    /// <summary>The tracker's localized name, resolved now (never cached).</summary>
    public static string Label(TrackerId? id) =>
        Helpers.LocalizationManager.Instance.GetString(For(id).LabelKey);
}
