namespace Animal_Diary_App.Data.Models;

/// <summary>
/// How one owner-created tracker looks: the custom counterpart to
/// <see cref="TrackerVisuals"/>.
///
/// <para><b>There is no label key here, deliberately.</b> A shipped tracker's name is a
/// resource key resolved per read; a custom tracker's name is the owner's own text, shown
/// verbatim and never translated (see AI/coding-standards.md). Pretending user input is a
/// localization key is exactly the confusion that would end with someone running
/// <c>GetString(pet-owner-typed-text)</c>. Callers read <see cref="CustomTracker.Name"/>
/// directly.</para>
/// </summary>
/// <param name="Icon">The emoji the owner picked.</param>
/// <param name="TintKey">Icon-tile background on the Journal timeline and chips.</param>
/// <param name="RowTintKey">Icon-tile background on the Manage page's care-plan rows.</param>
/// <param name="RowInkKey">Icon foreground on those rows.</param>
public readonly record struct CustomVisual(
    string Icon,
    string TintKey,
    string RowTintKey,
    string RowInkKey);

/// <summary>
/// The fixed palettes a custom tracker can be built from: five colours and a page of
/// emoji.
///
/// <para><b>Why fixed:</b> the colours are the SAME five token pairs the shipped trackers
/// already wear (<see cref="TrackerVisuals"/>), so an owner's walk tracker sits beside the
/// weigh-in without either looking out of place, and every one of them follows a theme
/// change. A free colour picker would put hex literals in the database: unreachable by
/// any theme, and the exact debt AI/known-constraints.md is still paying off elsewhere.</para>
/// </summary>
public static class CustomTrackerVisuals
{
    /// <summary>One choosable colour. <see cref="Key"/> is what persists in
    /// <see cref="CustomTracker.ColorKey"/>: a short stable id, never a token name and
    /// never a hex, so the palette can be re-pointed without rewriting rows.</summary>
    public readonly record struct Swatch(string Key, string TintKey, string RowTintKey, string RowInkKey);

    /// <summary>The five colours, in picker order. Teal first: it is the app's own
    /// accent and the safe default for someone who doesn't care.</summary>
    public static IReadOnlyList<Swatch> Palette { get; } = new[]
    {
        new Swatch("teal", "TealTint", "TealTint", "TealDeep"),
        new Swatch("blue", "BlueTint", "BlueTint", "BlueInk"),
        new Swatch("honey", "HoneyWarmTint", "HoneyWarmTint", "HoneyDeep"),
        new Swatch("rose", "RoseTint", "RoseTint", "RoseDeep"),
        new Swatch("violet", "VioletTint", "VioletSoftTint", "Violet"),
    };

    /// <summary>The colour a tracker gets when the owner never chose one.</summary>
    public const string DefaultColorKey = "teal";

    /// <summary>The emoji offered by the picker. A deliberately small, concrete set,
    /// the things people actually write down about an animal, rather than a system
    /// keyboard, which on Android hands back anything at all (including multi-codepoint
    /// sequences that render as a box on the chip row).</summary>
    public static IReadOnlyList<string> Icons { get; } = new[]
    {
        "🐾", "🦮", "🎾", "🧼", "🚿", "🪥", "✂️", "💩",
        "🤢", "🌙", "🏃", "🦴", "🧴", "🩹", "⏱️", "📏",
    };

    /// <summary>The icon a tracker gets when the owner never chose one.</summary>
    public const string DefaultIcon = "🐾";

    /// <summary>Anything unrecognised falls to the default swatch rather than throwing,
    /// same posture as <see cref="TrackerVisuals.Fallback"/>. A row written by a newer
    /// build with a colour this one doesn't know must still render.</summary>
    public static Swatch SwatchFor(string? colorKey)
    {
        foreach (var s in Palette)
            if (s.Key == colorKey)
                return s;
        return Palette[0];
    }

    /// <summary>The look of a tracker whose definition could not be found: an entry
    /// outliving its row, which a hard purge (revoked cloud access) can produce. Neutral
    /// well colours, like <see cref="TrackerVisuals.Fallback"/>.
    ///
    /// <para>This must be a real value and never <c>default(CustomVisual)</c>: a record
    /// struct's default has NULL strings, and those would reach
    /// <c>AppColors.Resolve</c> and a <c>Label.Text</c>.</para></summary>
    public static readonly CustomVisual Fallback = new(DefaultIcon, "Well", "Well", "Ink");

    /// <summary>Icon + colour tokens for one definition. Null-tolerant on purpose: see
    /// <see cref="Fallback"/>.</summary>
    public static CustomVisual For(CustomTracker? tracker)
    {
        if (tracker is null)
            return Fallback;

        var s = SwatchFor(tracker.ColorKey);
        var icon = string.IsNullOrWhiteSpace(tracker.Icon) ? DefaultIcon : tracker.Icon;
        return new CustomVisual(icon, s.TintKey, s.RowTintKey, s.RowInkKey);
    }
}
