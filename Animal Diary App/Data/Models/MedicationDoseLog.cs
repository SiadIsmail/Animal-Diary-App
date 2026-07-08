namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>Outcome recorded for a single scheduled dose.</summary>
public enum DoseStatus
{
    Taken = 0,
    Skipped = 1,
    Missed = 2
}

/// <summary>
/// A durable adherence record for one scheduled dose — the answer to "was this
/// dose taken?". Unlike <see cref="ReminderInstance"/> (ephemeral scheduling
/// state, pruned after a week), dose logs are permanent history queryable for any
/// date.
///
/// A row exists only once a dose has an outcome (Taken/Skipped recorded by the
/// user; Missed written by a later reconciliation sweep). A scheduled dose with
/// no row is simply "not yet acted on". A dose is identified by
/// (<see cref="MedicationId"/>, <see cref="ScheduledDate"/>, <see cref="ScheduledTime"/>).
/// </summary>
public class MedicationDoseLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Two access paths: by medication+date (week dots, dose-key lookups) and by
    // pet+date (a day's whole checklist). Each gets its own composite index.
    [Indexed(Name = "IX_DoseLog_Med_Date", Order = 1)]
    public int MedicationId { get; set; }

    /// <summary>Denormalized so a day's doses for a pet can be queried directly.</summary>
    [Indexed(Name = "IX_DoseLog_Pet_Date", Order = 1)]
    public int PetId { get; set; }

    /// <summary>The local date the dose was scheduled for (date-only).</summary>
    [Indexed(Name = "IX_DoseLog_Med_Date", Order = 2)]
    [Indexed(Name = "IX_DoseLog_Pet_Date", Order = 2)]
    public DateTime ScheduledDate { get; set; }

    /// <summary>Which dose of the day (matches a <see cref="MedicationSchedule"/> time).</summary>
    public TimeSpan ScheduledTime { get; set; }

    public DoseStatus Status { get; set; }

    /// <summary>When the user recorded Taken/Skipped; null for sweep-written Missed rows.</summary>
    public DateTime? ResolvedAt { get; set; }
}
