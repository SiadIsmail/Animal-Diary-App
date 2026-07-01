namespace Animal_Diary_App.Data.ViewModels;

/// <summary>
/// One editable reminder time shown in the Add/Edit medication sheet. The
/// number of slots is driven by the chosen frequency; each slot is bound to its
/// own <see cref="TimePicker"/>.
/// </summary>
public class ReminderTimeSlot : BaseViewModel
{
    public ReminderTimeSlot(int index, TimeSpan time)
    {
        this.index = index;
        this.time = time;
    }

    private int index;
    public int Index
    {
        get => index;
        set
        {
            if (SetProperty(ref index, value))
                OnPropertyChanged(nameof(Label));
        }
    }

    /// <summary>Field heading shown above the picker, e.g. "REMINDER 2".</summary>
    public string Label => Animal_Diary_App.Helpers.LocalizationManager.Instance.Format("MedEdit_ReminderLabel", Index + 1);

    private TimeSpan time;
    public TimeSpan Time
    {
        get => time;
        set => SetProperty(ref time, value);
    }
}
