namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  The vet visit, as a thing the app knows about.
//
//  It is the one moment where a year of writing things down could pay off, and
//  until now the app had no idea it was coming. Knowing the date is what lets
//  Today say a word about it, what gives the summary a window to cover, and what
//  the ledger and the questions are eventually read against.
//
//  Three absences are deliberate:
//
//   • NO STATUS COLUMN. A visit is past if its date is in the past — derived, like
//     Pet.AgeYears and VetQuestion.IsOpen. A stored status is a second source of
//     truth that starts disagreeing with the calendar the moment a device sleeps
//     through midnight.
//   • NO PRACTICE OR VET ENTITY, no directory, no lookup. Practice and VetName are
//     free-text labels for context, exactly like AppetiteEntry.Food — recorded
//     without building a system around them.
//   • NO RECURRENCE. A visit happens; the next one is a different visit.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One appointment, past or upcoming.</summary>
public class VetVisit : ISyncable
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

    /// <summary>The day, date-only and LOCAL — the same convention as
    /// <see cref="MedicationDoseLog.ScheduledDate"/>. An appointment is a wall-clock
    /// thing; converting it to UTC would move it across midnight for half the world.</summary>
    public DateTime Date { get; set; }

    /// <summary><b>Nullable, and that is the design.</b> Owners often know the day and
    /// not the slot, and this app never fabricates an unknown part of a date — the same
    /// rule that leaves <c>Pet.BirthMonth</c> null rather than inventing January.</summary>
    public TimeSpan? Time { get; set; }

    /// <summary>Free text. Never matched on, never deduplicated into an entity.</summary>
    public string Practice { get; set; } = string.Empty;

    /// <summary>Free text, same as <see cref="Practice"/>.</summary>
    public string VetName { get; set; } = string.Empty;

    /// <summary>What the vet said, written afterwards, in the owner's words. It becomes
    /// the opening line of the next "since your last visit".</summary>
    public string VisitNote { get; set; } = string.Empty;

    /// <summary>The moment the visit sits at, for ordering. A visit with no time sorts
    /// at the start of its day — it still happens on that day.</summary>
    [Ignore]
    public DateTime When => Date.Date + (Time ?? TimeSpan.Zero);

    /// <summary>Past means the DAY is behind us. Derived, never stored — and by day
    /// rather than by the hour, so a morning appointment does not become "past" at
    /// lunchtime while the owner is still in the waiting room.</summary>
    [Ignore]
    public bool IsPast => Date.Date < DateTime.Today;

    /// <summary>Nothing has been written down about how it went.</summary>
    [Ignore]
    public bool NeedsNote => IsPast && string.IsNullOrWhiteSpace(VisitNote);

    /// <summary>
    /// When this visit's ONE reminder fires: the evening before, whatever time of day
    /// the visit itself is at — including a visit with no time at all, which is exactly
    /// the case an "N hours before" rule could not answer.
    ///
    /// <para>Late enough that the day is winding down and tomorrow is being thought
    /// about, early enough to still find the carrier and write a question down. It sits
    /// here beside <see cref="When"/> and <see cref="IsPast"/> because it is another
    /// moment derived from the date; whether to arm it at all is
    /// <c>AppointmentReminderScheduler</c>'s decision.</para>
    /// </summary>
    [Ignore]
    public DateTime ReminderAt => Date.Date.AddDays(-1) + RemindAt;

    private static readonly TimeSpan RemindAt = new(18, 0, 0);
}
