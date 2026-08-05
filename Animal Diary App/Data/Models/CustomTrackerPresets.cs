namespace Animal_Diary_App.Data.Models;

/// <summary>
/// Starting points for the "add your own" sheet: the handful of things owners write
/// down most, already named, coloured and set to a sensible cadence.
///
/// <para><b>These are presets, not built-in trackers.</b> Picking one fills the form in
/// and nothing more — the owner can rename it, recolour it, change its cadence, or ignore
/// the row entirely and type their own. Nothing downstream knows a preset was used; there
/// is no preset id on the saved row, and no code path anywhere asks "is this the walk
/// one?". That is the whole point: discoverability without a closed set.</para>
///
/// <para>The names are localization KEYS because a preset is app chrome until the moment
/// it is accepted. From then on the stored name is the owner's — it does not re-translate
/// when they switch language, exactly like a pet's name (see AI/coding-standards.md).</para>
/// </summary>
public static class CustomTrackerPresets
{
    /// <param name="NameKey">AppStrings key for the suggested name.</param>
    /// <param name="UnitKey">AppStrings key for the suggested unit, or null for a Tick.</param>
    /// <param name="InReport">Whether it starts out reaching the vet summary. This is the
    /// one field where the preset table earns its keep: only the owner can say whether a
    /// thing is clinical, but for the handful everybody records the answer is known in
    /// advance. Sick days and stools belong in front of a vet; walks, grooming and play
    /// are life, not medicine. A wrong guess costs one tap in the same sheet.</param>
    public readonly record struct Preset(
        string NameKey,
        string Icon,
        string ColorKey,
        CustomShape Shape,
        string? UnitKey,
        TrackerKind Kind,
        int PerDayCount,
        bool InReport);

    /// <summary>In offer order: the two most-asked-for first.
    ///
    /// <para>Cadences are chosen so nothing nags about something that isn't a routine.
    /// A walk is a daily habit; a groom is a weekly one; a poop and a sick episode are
    /// <see cref="TrackerKind.Event"/> — they happen or they don't, and the app must
    /// never put "sick yet?" on a to-do list.</para></summary>
    public static IReadOnlyList<Preset> All { get; } = new[]
    {
        new Preset("CustomPreset_Walk", "🦮", "teal", CustomShape.Amount, "CustomPreset_WalkUnit", TrackerKind.Daily, 0, InReport: false),
        new Preset("CustomPreset_Poop", "💩", "honey", CustomShape.Tick, null, TrackerKind.Event, 0, InReport: true),
        new Preset("CustomPreset_Groom", "🧼", "blue", CustomShape.Tick, null, TrackerKind.Weekly, 0, InReport: false),
        new Preset("CustomPreset_Sick", "🤢", "rose", CustomShape.Tick, null, TrackerKind.Event, 0, InReport: true),
        new Preset("CustomPreset_Play", "🎾", "violet", CustomShape.Amount, "CustomPreset_PlayUnit", TrackerKind.Daily, 0, InReport: false),
    };

    /// <summary>The preset's suggested name, resolved now — never cached, so a live
    /// language switch re-reads it (see AI/coding-standards.md on singleton VMs).</summary>
    public static string Name(Preset p) =>
        Helpers.LocalizationManager.Instance.GetString(p.NameKey);

    /// <summary>The preset's suggested unit, or empty for a Tick.</summary>
    public static string Unit(Preset p) =>
        p.UnitKey is null ? string.Empty : Helpers.LocalizationManager.Instance.GetString(p.UnitKey);
}
