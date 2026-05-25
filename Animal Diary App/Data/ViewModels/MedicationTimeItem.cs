namespace Animal_Diary_App.Data.ViewModels;

using System;

public class MedicationTimeItem : BaseViewModel
{
    private TimeSpan time;
    public TimeSpan Time
    {
        get => time;
        set => SetProperty(ref time, value);
    }
}
