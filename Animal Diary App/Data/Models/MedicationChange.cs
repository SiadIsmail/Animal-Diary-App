namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  The treatment ledger.
//
//  Until this table existed, a medication edit DESTROYED the thing it changed:
//  Dosage was overwritten in place, and MedicationDoseLog recorded that a dose was
//  resolved but never WHICH dose it was. "What was he on in March?": the single
//  question a vet asks about a treatment: was unanswerable on every device, and
//  it cannot be backfilled after the fact. So every change becomes a row.
//
//  Two properties are load-bearing and neither is an accident:
//
//   • THE ROW IS SELF-CONTAINED. MedicationName and Summary are rendered at the
//     moment of the change and stored as text, because the medication can later be
//     renamed, archived or deleted and the ledger must still read correctly. Same
//     reasoning as a custom tracker's stored unit, and as the vet report counting
//     scheduled doses from dose logs rather than from current schedule rows: a row
//     that describes the past must not be re-rendered from the present.
//
//   • IT HANGS OFF THE PET, NOT THE MEDICATION (PetScope.ByPetId here; a cascade
//     from `pets` in Postgres). A cascade from medications would delete the history
//     of the very thing whose history this exists to preserve: the same note
//     migration 0013 leaves on custom_entries, for the same reason.
//
//  It states facts and never a direction. A row says a dose went from one number to
//  another on a date; nothing here computes whether that was up, down, better or
//  worse, and nothing may juxtapose two counts either side of one.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>What happened to a medication. Stored as text (<c>[StoreAsText]</c> on
/// the enum type: a 1.9.172 requirement), so appending a member is safe and
/// renaming one orphans every row that carries it.</summary>
[StoreAsText]
public enum MedicationChangeKind
{
    /// <summary>The medication was created.</summary>
    Started,

    /// <summary>The amount or its unit changed.</summary>
    DoseChanged,

    /// <summary>The days and/or times it is given changed.</summary>
    ScheduleChanged,

    /// <summary>The owner renamed it. The old name lives on in the summary.</summary>
    Renamed,

    /// <summary>Retired: it stops being asked for, but its history stays.</summary>
    Archived,

    /// <summary>Un-retired.</summary>
    Restored,

    /// <summary>Deleted outright.</summary>
    Stopped
}

/// <summary>
/// One durable entry in a pet's treatment ledger: what changed, when, and what it
/// read before and after: in the words the app used at the time.
/// </summary>
public class MedicationChange : ISyncable
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

    /// <summary>The medication this described, as a local id: a convenience, never a
    /// dependency. It may point at a since-deleted (or, on a device that pulled the
    /// row before its medication, a never-present) medication, and every consumer must
    /// read <see cref="MedicationName"/> and <see cref="Summary"/> instead of joining.
    /// 0 means "not resolvable here".</summary>
    public int MedicationId { get; set; }

    /// <summary>When the edit happened. UTC, like every other clock the sync layer
    /// compares.</summary>
    public DateTime ChangedAtUtc { get; set; }

    public MedicationChangeKind Kind { get; set; }

    /// <summary>The medication's name as of this change: denormalized on purpose
    /// (see the file header). Verbatim user text; never translated.</summary>
    public string MedicationName { get; set; } = string.Empty;

    /// <summary>The fact, already rendered: <c>"30 mg → 45 mg"</c>. Empty where the
    /// kind IS the whole fact (archived, restored, stopped): there is no value to
    /// state and inventing one would pad the ledger with noise.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Optional owner text about the change. Nothing writes it yet; the
    /// column exists so recording "vet raised it after the June bloods" later costs
    /// no migration.</summary>
    public string Note { get; set; } = string.Empty;
}
