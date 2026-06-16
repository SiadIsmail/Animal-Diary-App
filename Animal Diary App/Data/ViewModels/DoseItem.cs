namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;

/// <summary>
/// One scheduled dose shown in the Calendar day checklist. Carries the identity
/// needed to record an outcome (medication + date + time) plus display state.
/// </summary>
public class DoseItem : BaseViewModel
{
    public int MedicationId { get; init; }
    public int PetId { get; init; }
    public DateTime ScheduledDate { get; init; }
    public TimeSpan ScheduledTime { get; init; }
    public string MedName { get; init; } = string.Empty;
    public string DoseDisplay { get; init; } = string.Empty;

    /// <summary>False for future doses — you can't take a dose early.</summary>
    public bool CanToggle { get; init; }

    public string TimeDisplay => ScheduledTime.ToString(@"hh\:mm");

    private DoseStatus? status;
    /// <summary>Null = no outcome recorded yet (upcoming or not-yet-taken).</summary>
    public DoseStatus? Status
    {
        get => status;
        set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(IsTaken));
                OnPropertyChanged(nameof(IsSkipped));
                OnPropertyChanged(nameof(ShowSkip));
                OnPropertyChanged(nameof(StatusGlyph));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private DateTime? resolvedAt;
    public DateTime? ResolvedAt
    {
        get => resolvedAt;
        set
        {
            if (SetProperty(ref resolvedAt, value))
                OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsTaken => Status == DoseStatus.Taken;
    public bool IsSkipped => Status == DoseStatus.Skipped;

    /// <summary>Offer the secondary "skip" action only when actionable and not already taken.</summary>
    public bool ShowSkip => CanToggle && Status != DoseStatus.Taken;

    public string StatusGlyph => Status switch
    {
        DoseStatus.Taken => "✓",
        DoseStatus.Skipped => "–",
        _ => string.Empty
    };

    public string StatusText => Status switch
    {
        DoseStatus.Taken => ResolvedAt.HasValue ? $"Taken · {ResolvedAt.Value:HH:mm}" : "Taken",
        DoseStatus.Skipped => "Skipped",
        DoseStatus.Missed => "Missed",
        _ => CanToggle ? "Not taken" : "Upcoming"
    };
}
