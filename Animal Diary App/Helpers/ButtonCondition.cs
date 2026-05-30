namespace Animal_Diary_App.Helpers;
using Animal_Diary_App.Data.ViewModels;
using System.ComponentModel;
using System.Runtime.CompilerServices;
public class EntrySection : BaseViewModel
{
    private bool isAddButtonVisible = true;
    public bool IsAddButtonVisible
    {
        get => isAddButtonVisible;
        set
        {
            isAddButtonVisible = value;
            OnPropertyChanged();
        }
    }

    private bool isInputVisible;
    public bool IsInputVisible
    {
        get => isInputVisible;
        set
        {
            isInputVisible = value;
            OnPropertyChanged();
        }
    }

    
    public static void ShowInput(EntrySection section, EntrySection entrySection1, EntrySection entrySection2)
    {
        HideInput(entrySection1);
        HideInput(entrySection2);
        section.IsAddButtonVisible = false;
        section.IsInputVisible = true;
    }
    public static void HideInput(EntrySection section)
    {
        section.IsAddButtonVisible = true;
        section.IsInputVisible = false;
    }
}


