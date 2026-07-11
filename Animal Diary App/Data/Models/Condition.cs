namespace Animal_Diary_App.Data.Models;

/// <summary>
/// The kind of value a <see cref="TrackingItem"/> records. The Calendar's
/// DataTemplateSelector renders one row layout per kind, and
/// <see cref="TrackingEntry"/> stores each kind in the matching column(s).
/// Adding a brand-new kind means: add it here, add a column mapping in the row
/// ViewModel, and add one DataTemplate in CalendarPage — nothing else.
/// </summary>
public enum InputKind
{
    /// <summary>Given / skipped, with the time it happened (e.g. an insulin shot).</summary>
    Dose,
    /// <summary>A single measured number, usually with a unit (e.g. blood glucose mg/dL).</summary>
    Numeric,
    /// <summary>A 1..N rating (e.g. appetite 1–5).</summary>
    Scale,
    /// <summary>A volume in millilitres (e.g. water intake, sub-Q fluids).</summary>
    Volume,
    /// <summary>A timed occurrence with duration + severity + note (e.g. a seizure).</summary>
    Event,
    /// <summary>A yes / no occurrence (e.g. vomiting).</summary>
    Boolean,
    /// <summary>Free-text note.</summary>
    Text,
    /// <summary>The bespoke 5-emoji mood picker (+ optional note). Native — persists
    /// to PetEntry, not TrackingEntry; only the Tracker Hub uses this kind.</summary>
    Mood
}

/// <summary>One ongoing condition a pet can have. Pure data — see
/// <see cref="ConditionCatalog"/> for the list and the items each maps to.</summary>
public record Condition(string Id, string Name, string Icon);

/// <summary>
/// Describes ONE loggable field: what it's called, how it's entered, and its
/// optional unit / icon. These are config, not UI — the Calendar builds its
/// input rows from a list of these.
/// </summary>
public class TrackingItem
{
    public TrackingItem(
        string id,
        string name,
        InputKind kind,
        string icon = "",
        string unit = "",
        bool isNative = false,
        int scaleMax = 5)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Icon = icon;
        Unit = unit;
        IsNative = isNative;
        ScaleMax = scaleMax;
    }

    /// <summary>Stable key. Used as the <see cref="TrackingEntry.ItemId"/> and to
    /// share one item definition across several conditions.</summary>
    public string Id { get; }

    public string Name { get; }
    public InputKind Kind { get; }
    public string Icon { get; }

    /// <summary>Optional unit shown next to the value (e.g. "mg/dL", "ml").</summary>
    public string Unit { get; }

    /// <summary>True when this item is already logged by the Calendar's existing
    /// bespoke UI (Weight, Mood, scheduled Medication). The dynamic tracker skips
    /// these so a pet's weight/mood/meds aren't recorded in two places.</summary>
    public bool IsNative { get; }

    /// <summary>Top of the range for <see cref="InputKind.Scale"/> items.</summary>
    public int ScaleMax { get; }
}
