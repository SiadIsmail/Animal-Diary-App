namespace Animal_Diary_App.Data.ViewModels;

using System;

public class DaySelectionItem : BaseViewModel
{
    public DayOfWeek Day { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
