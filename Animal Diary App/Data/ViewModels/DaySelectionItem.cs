namespace Animal_Diary_App.Data.ViewModels;

using System;
using Animal_Diary_App.Helpers;

public class DaySelectionItem : BaseViewModel
{
    public DayOfWeek Day { get; set; }

    /// <summary>AppStrings key for the day's short name (e.g. "Day_Mon"). The
    /// display name is resolved per read so a live language switch re-translates
    /// the chips (the VM owning these is a singleton — a name cached at
    /// construction would stay in the old language).</summary>
    public string ResourceKey { get; set; } = string.Empty;

    public string DisplayName => LocalizationManager.Instance.GetString(ResourceKey);

    /// <summary>Re-raise <see cref="DisplayName"/> after a language switch.</summary>
    public void RefreshDisplayName() => OnPropertyChanged(nameof(DisplayName));

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
