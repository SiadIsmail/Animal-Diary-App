namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
public class MedicationViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private string enteredMedicationName = string.Empty;
    public string EnteredMedicationName
    {
        get => enteredMedicationName;
        set
        {
            if (enteredMedicationName == value) return;
            enteredMedicationName = value;
            OnPropertyChanged();
        }
    }
    private string enteredDosage = string.Empty;
    public string EnteredDosage
    {
        get => enteredDosage;
        set
        {
            if (enteredDosage == value) return;
            enteredDosage = value;
            OnPropertyChanged();
        }


    }
}