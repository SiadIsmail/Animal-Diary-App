namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  The Care Plan.
//
//  A pet's care plan is the list of things the Journal will gently ask for. It is
//  the ONLY thing the Journal knows about: the Journal never sees a condition
//  name. Conditions (Diabetes, CKD…) are just the reason a tracker was added; once
//  added, a tracker stands on its own (see Tracker.FromCondition, which is only a
//  breadcrumb back to the condition that introduced it).
//
//  Medications are NOT trackers. Scheduled doses live entirely in the existing
//  Medication / MedicationSchedule / MedicationDoseLog model; the pending engine
//  reads them from there. Insulin is a medication like any other, never special.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Which loggable thing a <see cref="Tracker"/> represents. A closed set,
/// the Journal renders one input sheet per value here. Stored as text so the enum
/// can be reordered/extended without rewriting existing rows.</summary>
[StoreAsText]
public enum TrackerId
{
    Glucose,
    Mood,
    Appetite,
    Weight,
    Water,
    Seizure
}

/// <summary>How often a tracker is expected: its cadence. Drives the pending
/// engine's "is this due today?" logic (see the PendingItems service, Step 2).</summary>
[StoreAsText]
public enum TrackerKind
{
    /// <summary>N checks every day, e.g. glucose 3× daily. Uses <see cref="Tracker.PerDayCount"/>.</summary>
    PerDay,
    /// <summary>Once a day.</summary>
    Daily,
    /// <summary>Once in a rolling 7-day window.</summary>
    Weekly,
    /// <summary>Once in a rolling 3-day window.</summary>
    TwiceWeekly,
    /// <summary>Logged only when the owner feels like it, never appears in "Still to do".</summary>
    AsNeeded,
    /// <summary>Logged as it happens, e.g. a seizure, never appears in "Still to do".</summary>
    Event
}

/// <summary>An inclusive low–high band, e.g. a vet's target glucose range. Pure
/// value type; precise decimals in, precise decimals out, never rounded.</summary>
public readonly record struct TargetRange(decimal Lo, decimal Hi)
{
    public bool Contains(decimal value) => value >= Lo && value <= Hi;
}

/// <summary>
/// One entry in a pet's care plan: a value to log at a given cadence. Persisted so
/// the (later) pet page can let owners tune frequency, target range and on/off per
/// pet; new pets are seeded from <see cref="CarePlanCatalog"/>.
///
/// The primary key <see cref="Id"/> is the row id; <see cref="TrackerId"/> is which
/// value this is (glucose, mood…).
/// </summary>
public class Tracker : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed]
    public int PetId { get; set; }

    /// <summary>Which value this tracker records.</summary>
    public TrackerId TrackerId { get; set; }

    /// <summary>The cadence: how the pending engine decides if it's due.</summary>
    public TrackerKind Kind { get; set; }

    /// <summary>Checks expected per day when <see cref="Kind"/> is
    /// <see cref="TrackerKind.PerDay"/> (e.g. 3 for glucose 3× daily); 0 otherwise.</summary>
    public int PerDayCount { get; set; }

    /// <summary>Target-range low bound, <b>in <see cref="Unit"/></b>, not in the app's
    /// canonical unit. Only meaningful for glucose. Null (together with
    /// <see cref="TargetHi"/>) means "no range yet": readings are simply recorded,
    /// without judgement, until a range is added.</summary>
    public decimal? TargetLo { get; set; }
    public decimal? TargetHi { get; set; }

    /// <summary>
    /// The unit the <b>target band</b> was entered in: a stable id from
    /// <see cref="UnitCatalog"/> ("mmol_l", "mg_dl"), empty for a tracker with no band.
    ///
    /// <para>This is the one place a stored number is NOT canonical, and deliberately: a
    /// band is a pair the owner typed once, quoting their vet, and it is never summed,
    /// charted or compared across pets, so there is nothing for a canonical form to make
    /// comparable. What matters is that it can be converted for display, which needs its
    /// source unit recorded, which is exactly this column.</para>
    ///
    /// <para><b>Legacy rows hold the LABEL "mmol/L" rather than an id</b>, from before
    /// units had ids. That is harmless and needs no migration: an unrecognised id
    /// resolves to the family's canonical unit, and mmol/L <i>is</i> the canonical unit,
    /// so those rows read back as exactly what they always meant. Use
    /// <see cref="TargetUnit"/> rather than reading this string directly.</para>
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Id of the <see cref="Condition"/> that introduced this tracker, or
    /// null for the always-on defaults (Mood, Weight). A breadcrumb only: the
    /// Journal never reads it.</summary>
    public string? FromCondition { get; set; }

    /// <summary>The target band as a value, or null when no range is set. Convenience
    /// over the two nullable columns. Stated in <see cref="TargetUnit"/>.</summary>
    [Ignore]
    public TargetRange? TargetRange =>
        TargetLo.HasValue && TargetHi.HasValue
            ? new TargetRange(TargetLo.Value, TargetHi.Value)
            : null;

    /// <summary>The unit <see cref="TargetRange"/> is stated in, resolved through the
    /// catalog so a legacy label and a missing value both land on the canonical unit.
    /// Null for a tracker whose readings have no convertible unit at all.</summary>
    [Ignore]
    public UnitDef? TargetUnit => UnitCatalog.FamilyFor(TrackerId) is UnitFamily f
        ? UnitCatalog.Get(f, Unit)
        : null;
}

/// <summary>
/// One line of a pet's care plan, whatever produced it: a shipped <see cref="Tracker"/>
/// row or a tracker the owner made up themselves.
///
/// <para><b>Why this exists rather than everything being a <see cref="Tracker"/>:</b> a
/// custom tracker already needs a row of its own (its name, icon and shape), and giving
/// it a second row in <c>Tracker</c> to carry its cadence would mean two rows, two
/// tombstones and two chances to orphan one half. So the cadence lives on the
/// definition, and this is the shape both sources project into. The plan stays one
/// list, read through one seam (<c>CarePlanService.GetPlanAsync</c>), exactly as before.</para>
///
/// <para>It is a read-model: nothing persists a <c>CarePlanItem</c>. Write through
/// whichever store owns the row.</para>
/// </summary>
public sealed record CarePlanItem
{
    /// <summary>Which tracker this line is for: the identity every other subsystem
    /// keys on.</summary>
    public required TrackerKey Key { get; init; }

    /// <summary>The cadence, driving the pending engine's "is this due today?".</summary>
    public required TrackerKind Kind { get; init; }

    /// <summary>Checks expected per day when <see cref="Kind"/> is
    /// <see cref="TrackerKind.PerDay"/>; 0 otherwise.</summary>
    public int PerDayCount { get; init; }

    /// <summary>The glucose target band, or null, stated in <see cref="TargetUnit"/>
    /// rather than in the canonical unit (see <see cref="Tracker.Unit"/>). Only ever set
    /// for glucose: a custom tracker never carries one, because a range is a clinical
    /// judgement the app has no business inventing for a value the owner defined.</summary>
    public TargetRange? TargetRange { get; init; }

    /// <summary>For a shipped tracker, the stable unit id the target band was entered in;
    /// for an owner-defined one, their own free text ("min", "km"), which nothing
    /// converts. Empty when there is neither.</summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>The unit <see cref="TargetRange"/> is stated in, or null for a record
    /// with no convertible unit (including every custom tracker, whose unit is the
    /// owner's own word).</summary>
    public UnitDef? TargetUnit => !Key.IsCustom && UnitCatalog.FamilyFor(Key.BuiltIn!.Value) is UnitFamily f
        ? UnitCatalog.Get(f, Unit)
        : null;

    /// <summary>Id of the condition that introduced this line, or null. Always null for
    /// a custom tracker: no condition may ever claim one.</summary>
    public string? FromCondition { get; init; }

    /// <summary>Project a persisted built-in tracker row.</summary>
    public static CarePlanItem FromTracker(Tracker t) => new()
    {
        Key = t.TrackerId,
        Kind = t.Kind,
        PerDayCount = t.PerDayCount,
        TargetRange = t.TargetRange,
        Unit = t.Unit,
        FromCondition = t.FromCondition,
    };
}
