namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  Trackers the owner made up.
//
//  A walk, a groom, a poop, a nail trim. The app ships six trackers because each
//  one earns a bespoke input sheet, a condition seed or its own shape in the vet
//  report. Everything else an owner wants to write down is the same thing wearing
//  a different name: it happened, at a time, sometimes with a number, sometimes
//  with a sentence. So it is DATA rather than code — one definition row the owner
//  creates, and events pointing at it.
//
//  The cadence lives HERE rather than in a sibling Tracker row. A Tracker row would
//  mean two rows, two tombstones and two chances to orphan one half — and the
//  trackers table converges on (pet_id, tracker_id) in the cloud, so every custom
//  one would collapse onto a single row on a second device. CarePlanService merges
//  the two sources instead (see CarePlanItem).
//
//  Deliberately absent, and not oversights:
//   • No target range. A range is a clinical judgement; the app has no business
//     inventing one for a value the owner defined (AI/design-decisions.md).
//   • No FromCondition. A condition may never claim a custom tracker.
//   • No reminders. The daily care nudge already covers "something's left today".
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What a custom tracker records. Two shapes cover everything asked for so
/// far, and the optional note on every entry carries the rest — "the walk was great"
/// is a sentence, not a scale (mirrors <see cref="AppetiteEntry.Food"/>, which records
/// context without building a system around it).</summary>
[StoreAsText]
public enum CustomShape
{
    /// <summary>It happened. Grooming, a nail trim, a poop.</summary>
    Tick,

    /// <summary>A number in the owner's own unit — 35 minutes, 2 bowls.</summary>
    Amount
}

/// <summary>
/// One tracker the owner defined: what it is called, how it looks, what it records
/// and how often the Journal should ask for it.
///
/// <para><see cref="Name"/> is <b>user data</b> — shown verbatim, never translated,
/// never fed into a sentence that assumes grammar (see AI/coding-standards.md).</para>
/// </summary>
public class CustomTracker : ISyncable
{
    /// <summary>How many custom trackers one pet may have at once.
    ///
    /// <para>Not scarcity — the chip row, the "+" sheet and the report section each have
    /// to stay a readable page, and an owner with forty trackers has built a form, not a
    /// diary. Archived ones don't count: retiring one must always make room.</para>
    ///
    /// <para>It lives on the model rather than on <c>CustomTrackerService</c> because it
    /// is a rule about the thing, not about the store — and the import validator, which
    /// enforces the same cap, is deliberately free of anything that opens a database.</para></summary>
    public const int MaxPerPet = 10;

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

    /// <summary>What the owner called it ("Walk", "Ohren putzen"). Verbatim user text.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The emoji shown on its chip, timeline tile and care-plan row.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>A colour token NAME from <c>Resources/Styles/Colors.xaml</c>, picked
    /// from a fixed palette — never a hex literal, which no theme change could reach
    /// (see <c>Helpers/AppColors</c>).</summary>
    public string ColorKey { get; set; } = string.Empty;

    public CustomShape Shape { get; set; }

    /// <summary>The owner's unit for an <see cref="CustomShape.Amount"/> tracker
    /// ("min", "km", "bowls"); empty for a Tick. Free text, never a unit system.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>How often the Journal asks — the same cadence vocabulary the shipped
    /// trackers use, so the pending engine needs no new rule.</summary>
    public TrackerKind Kind { get; set; }

    /// <summary>Checks expected per day when <see cref="Kind"/> is
    /// <see cref="TrackerKind.PerDay"/>; 0 otherwise.</summary>
    public int PerDayCount { get; set; }

    /// <summary>
    /// Whether this tracker's entries reach the vet summary.
    ///
    /// <para>Felova cannot answer this: a walk is noise for one owner and the whole point
    /// for another whose dog has a limp. So the owner decides, ONCE, on the tracker rather
    /// than per export (the answer doesn't change between exports) or per entry (which
    /// would be the same question 200 times).</para>
    ///
    /// <para><b>Default true</b>, because the two failure modes are not symmetric. A stray
    /// "Walk" section is something a vet skims past. A tracker of vomiting that silently
    /// never reached the summary is the app failing at its one job, at the appointment
    /// where it mattered. The switch sits in the create sheet, so nobody naming a tracker
    /// "Walk" is unaware of it.</para>
    ///
    /// <para>Reading it at build time makes changes <b>retroactive both ways</b> — turning
    /// it on next month includes everything already written down. That is what "you decide
    /// what your vet sees" has to mean.</para>
    /// </summary>
    public bool IncludeInReport { get; set; } = true;

    /// <summary>Retired by the owner: it stops being asked for and leaves the "+" sheet,
    /// but every entry it ever collected stays readable and keeps reaching the vet
    /// report. Mirrors <see cref="Medication.IsArchived"/> — turning a tracker off has
    /// never deleted data in this app, and a custom one must not be the exception.</summary>
    public bool IsArchived { get; set; }

    /// <summary>This tracker's identity everywhere else in the app.</summary>
    [Ignore]
    public TrackerKey Key => TrackerKey.Custom(Id);

    /// <summary>Project onto the care plan. <c>FromCondition</c> stays null forever.</summary>
    public CarePlanItem ToCarePlanItem() => new()
    {
        Key = Key,
        Kind = Kind,
        PerDayCount = PerDayCount,
        Unit = Unit,
    };
}

/// <summary>
/// One occurrence of a custom tracker. <b>Event-shaped</b> — many per day, each its
/// own row, never upserted, converging on <c>id</c> in the cloud. That is what lets a
/// single table serve every custom tracker: one conflict key, no natural key to
/// invent, and "replace today's" (if a cadence ever wants it) is an honest
/// delete-then-insert rather than a revived tombstone.
/// </summary>
public class CustomEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    // PetId is carried alongside CustomTrackerId (rather than reached through it) so
    // the row is selectable by pet in one predicate — which is what SyncedTables'
    // PetScope.ByPetId, the reset, the tombstone cascade and the revoked-access purge
    // all need.
    [Indexed(Name = "IX_CustomEntry_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    [Indexed(Name = "IX_CustomEntry_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }

    /// <summary>The <see cref="CustomTracker"/> this records.</summary>
    [Indexed]
    public int CustomTrackerId { get; set; }

    public TimeSpan Time { get; set; }

    /// <summary>The number for an <see cref="CustomShape.Amount"/> tracker, stored
    /// exactly; null for a Tick (which records only that it happened).</summary>
    public decimal? Amount { get; set; }

    /// <summary>Optional free text the owner added ("great walk, no limp"), or empty.
    /// Context only — never parsed, scored or turned into a category.</summary>
    public string Note { get; set; } = string.Empty;
}
