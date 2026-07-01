namespace Animal_Diary_App.Data.Models;

using SQLite;

/// <summary>Lifecycle state of a single concrete reminder occurrence.</summary>
public enum ReminderStatus
{
    Pending = 0,
    Fired = 1,
    Missed = 2,
    Cancelled = 3
}

/// <summary>
/// A single, concrete reminder occurrence — the materialized form of a
/// recurring <see cref="MedicationSchedule"/> rule.
///
/// Recurrence ("Mon &amp; Thu at 09:00") lives only in the rule layer; at runtime
/// it is expanded into independent <see cref="ReminderInstance"/> rows, each
/// representing one exact wall-clock trigger that can be scheduled, cancelled,
/// fired, or marked missed on its own. This is what makes scheduling reliable
/// under Android background limits — we never rely on infinite OS recurrence.
/// </summary>
public class ReminderInstance
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public int MedicationId { get; set; }

    /// <summary>Exact local wall-clock time this reminder should fire.</summary>
    public DateTime ScheduledTime { get; set; }

    /// <summary>The OS notification id used for this instance (see NotificationIds).</summary>
    public int NotificationId { get; set; }

    public ReminderStatus Status { get; set; } = ReminderStatus.Pending;

    /// <summary>Index of this time among the medication's daily times — used to vary the message copy.</summary>
    public int SlotIndex { get; set; }
}
