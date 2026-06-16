namespace Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Kind of activity surfaced as a calendar dot. Event-based (not boolean flags)
/// so new indicator types only need a new value here plus a mapping in the
/// ViewModel — no UI or schema change. <see cref="Symptoms"/>/<see cref="VetVisit"/>
/// are reserved for future data models and produce no dots yet.
/// </summary>
public enum CalendarActivityType
{
    Medication,
    Weight,
    Mood,
    Symptoms,
    VetVisit
}

/// <summary>
/// Whether the activity is planned or done. Drives the dot style:
/// <see cref="Scheduled"/> → hollow ring, <see cref="Completed"/> → filled dot.
/// </summary>
public enum CalendarActivityState
{
    Scheduled,
    Completed
}

/// <summary>
/// One indicator on a calendar day, handed from the ViewModel to the calendar
/// control. The control groups these by <see cref="Date"/> and renders dots; it
/// never interprets <see cref="Type"/> beyond colour/shape. A day may carry
/// several activities (e.g. a hollow medication dot and a filled weight dot).
/// </summary>
public sealed class CalendarActivity
{
    /// <summary>Date-only.</summary>
    public DateTime Date { get; init; }
    public CalendarActivityType Type { get; init; }
    public CalendarActivityState State { get; init; }
}
