namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private MedicationService _medicationService;
    public EntrySection MedicationSection { get; } = new();

    public MedicationViewModel(MedicationService medicationService)
    {
        _medicationService = medicationService;
    }
    private decimal ParseDosage()
    {
        if (InputParser.TryParsePositive(EnteredDosage, out var dosage))
        {
            return dosage;
        }
        return 0; // Default value if parsing fails
    }
    public async Task SaveMedicationEntryAsync()
    {
        var newMedicationLog = new MedicationLog
        {
            MedicationName = EnteredMedicationName,
            Dosage = ParseDosage()
        };
        EnteredMedicationName = string.Empty;
        EnteredDosage = string.Empty;

        await _medicationService.SaveMedicationEntryAsync(newMedicationLog);
    }


}