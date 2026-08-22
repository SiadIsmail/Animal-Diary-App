namespace Animal_Diary_App.Data.Services.Import;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  What the validator is allowed to know about the device.
//
//  The validator is PURE (no SQLite, no MAUI) so everything it needs about what is
//  already stored arrives as this snapshot, assembled by ImportService from ordinary
//  repository reads. Same seam shape Billing uses for its cloud facts (IPetAccessSource
//  and friends): the pure half declares what it needs, the impure half supplies it.
//
//  It is also why the validator can decide skips at all. "This day already has a
//  weight" is a fact about the database, and the preview has to state it BEFORE the
//  owner confirms, so the reads happen up front, once, over only the date range the
//  file actually covers.
//
//  Deliberately NOT modelled here: Pet itself. Pet.cs resolves a photo path through
//  PetPhotoService, which makes the file unlinkable by the MAUI-free test project (its
//  own comment says so). ExistingPet carries the three fields matching needs instead.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A pet already on the device that a file may append to. Demo pets are
/// excluded by the caller: importing into a filming fixture would write rows that can
/// never sync and would pollute a pet the creator is about to delete.</summary>
public sealed record ExistingPet(int Id, string Name, string Species);

/// <summary>A tracker the pet already has. Archived ones are included on purpose: an
/// import matches against them so a retired "Walk" is reused rather than duplicated,
/// and the entries still reach the timeline and the vet report, which both read the
/// archived-inclusive definition list.
///
/// <para><see cref="Shape"/> is carried so a file that disagrees with the device about
/// what a tracker records can be caught. Writing amounts into a tracker the owner
/// created as a tick produces entries the chip row and the report cannot render as the
/// owner meant them, and nothing downstream would ever flag it.</para></summary>
public sealed record ExistingTracker(int Id, int PetId, string Name, CustomShape Shape, bool IsArchived);

/// <summary>
/// The pet's <c>PetEntry</c> row for one date, if it has one.
///
/// <para>This one is shaped differently from the others because <c>PetEntry</c> is a
/// single row per day holding TWO independent halves: mood and weight. A day can be
/// half-full, and filling the empty half is additive rather than destructive, so the
/// two are tracked separately.</para>
/// </summary>
/// <param name="RowId">The row to update. Never 0: a day with no row produces no
/// snapshot entry at all.</param>
/// <param name="IsTombstone">The row was soft-deleted (an undone log). It must be
/// REVIVED in place rather than joined by a sibling: the cloud keys this table by
/// (pet, day), so a second row would collapse onto the first on the next pull.</param>
public sealed record ExistingPetDay(
    int PetId,
    DateTime Date,
    int RowId,
    bool HasMood,
    bool HasWeight,
    bool IsTombstone);

/// <summary>A one-per-day level row the pet already has: appetite_level or
/// water_level. Same tombstone rule as <see cref="ExistingPetDay"/>, minus the
/// two-halves complication (these rows carry a single value).</summary>
public sealed record ExistingLevelDay(
    int PetId,
    ImportEntryType Type,
    DateTime Date,
    int RowId,
    bool IsTombstone);

/// <summary>
/// An event row already stored, reduced to the fields that identify it.
///
/// <para>Used for the "already present" skip: a seizure at 20:00 on the 10th lasting a
/// minute, imported twice, is one seizure. This is what makes re-running a regenerated
/// file safe: the hash guard only catches byte-identical re-imports, and an AI asked
/// the same question twice never produces identical bytes.</para>
///
/// <para>It can, in principle, drop a genuine second reading taken in the same minute
/// with the same value. That trade is deliberate and it is the safe direction: a
/// duplicated medical event misleads a vet, a missing duplicate of an identical reading
/// does not, and the preview names every skip before anything is written.</para>
/// </summary>
/// <param name="TrackerId">The custom tracker this belongs to, or 0 for a built-in
/// type. Without it, two different custom trackers ticked at the same minute would
/// fingerprint identically and the second would be skipped.</param>
/// <param name="Value">The row's defining number <b>in the store's canonical unit</b>
/// (kg, mmol/L, mL, g, seconds), or 0 for the types that have none (a seizure with no
/// recorded duration, a tick). Canonical on both sides is what makes the fingerprint
/// comparable at all: an incoming 11.4 lb and a stored 5.17 kg are the same reading, and
/// comparing them as typed would import the duplicate every time.</param>
public sealed record ExistingEvent(
    int PetId,
    ImportEntryType Type,
    DateTime Date,
    TimeSpan Time,
    decimal Value,
    int TrackerId = 0);

/// <summary>Everything the validator may consult about the device's current contents.</summary>
public sealed class ImportSnapshot
{
    public required IReadOnlyList<ExistingPet> Pets { get; init; }

    public IReadOnlyList<ExistingTracker> Trackers { get; init; } = Array.Empty<ExistingTracker>();

    public IReadOnlyList<ExistingPetDay> PetDays { get; init; } = Array.Empty<ExistingPetDay>();

    public IReadOnlyList<ExistingLevelDay> LevelDays { get; init; } = Array.Empty<ExistingLevelDay>();

    public IReadOnlyList<ExistingEvent> Events { get; init; } = Array.Empty<ExistingEvent>();

    /// <summary>A snapshot for a device with nothing on it: every pet block must say
    /// "new", and nothing can collide. The shape a first-run import sees.</summary>
    public static ImportSnapshot Empty { get; } = new() { Pets = Array.Empty<ExistingPet>() };
}
