namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>What produced a pending item — a scheduled medication dose, or a
/// care-plan tracker that's still due.</summary>
public enum PendingKind
{
    Medication,
    Tracker
}

/// <summary>
/// One thing still to do today, as computed by <see cref="PendingEngine"/>. This is
/// the data behind a single "Still to do" chip. It is a plain value with no UI or
/// persistence concerns — the Journal maps it to a chip and to the right sheet.
///
/// The word "Diabetes" (or any condition) never reaches here: a pending item knows
/// only a tracker or a med dose.
/// </summary>
public sealed record PendingItem
{
    public PendingKind Kind { get; init; }

    // ── Medication doses ──────────────────────────────────────────────────────
    /// <summary>The medication (0 for tracker items).</summary>
    public int MedicationId { get; init; }
    public int PetId { get; init; }
    /// <summary>Medication name, e.g. "Insulin" — shown on the chip as "{name} · {time}".</summary>
    public string MedicationName { get; init; } = string.Empty;
    /// <summary>The scheduled time of this dose; null for tracker items.</summary>
    public TimeSpan? DoseTime { get; init; }

    // ── Trackers ──────────────────────────────────────────────────────────────
    /// <summary>Which tracker (null for medication items).</summary>
    public TrackerId? TrackerId { get; init; }

    /// <summary>For PerDay trackers (glucose): how many of the day's checks are done
    /// and how many are wanted — the chip shows "{Done} of {Target}". Both 0 for
    /// non-PerDay items.</summary>
    public int Done { get; init; }
    public int Target { get; init; }

    internal static PendingItem ForDose(int petId, int medicationId, string name, TimeSpan time) => new()
    {
        Kind = PendingKind.Medication,
        PetId = petId,
        MedicationId = medicationId,
        MedicationName = name,
        DoseTime = time
    };

    internal static PendingItem ForTracker(TrackerId id, int done = 0, int target = 0) => new()
    {
        Kind = PendingKind.Tracker,
        TrackerId = id,
        Done = done,
        Target = target
    };
}

/// <summary>A scheduled dose on the target day, with whether it has already been
/// given — the medication input to <see cref="PendingEngine.Compute"/>. Keeping this
/// as a flat snapshot is what lets the engine stay pure and unit-testable: the
/// async service resolves "given?" from the dose logs and hands over plain data.</summary>
public sealed record ScheduledDose(int MedicationId, int PetId, string MedicationName, TimeSpan Time, bool Given);
