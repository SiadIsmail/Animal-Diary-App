namespace Animal_Diary_App.Helpers;

using System.ComponentModel;
using System.Runtime.CompilerServices;
public class EntrySection : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
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
    public static void ShowInput(EntrySection section)
    {
        section.IsAddButtonVisible = false;
        section.IsInputVisible = true;
    }
    public static void HideInput(EntrySection section)
    {
        section.IsAddButtonVisible = true;
        section.IsInputVisible = false;
    }
}


