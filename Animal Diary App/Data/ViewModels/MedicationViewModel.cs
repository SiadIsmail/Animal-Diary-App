namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Collections.Generic;
public class MedicationViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
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
    public List<int> FrequencyOptions { get; } = new()
    {
        1,
        2,
        3,
        4,
        5
    };

    private Medication medicationDraft = new();
    public Medication MedicationDraft
    {
        get => medicationDraft;
        set
        {
            if (medicationDraft == value) return;
            medicationDraft = value;
            OnPropertyChanged();
        }
    }
    private MedicationService _medicationService;
    public MedicationViewModel(MedicationService medicationService)
    {
        _medicationService = medicationService;
    }
    private decimal ParseDosage()
    {
        if (InputParser.TryParsePositive(MedicationDraft.Dosage.ToString(), out var dosage))
        {
            return dosage;
        }
        return 0;
    }
    public async Task SaveMedicationCommandasync()
    {
        var newMedication = new Medication
        {
            Name = MedicationDraft.Name,
            Dosage = ParseDosage(),
            PetId = 1 // Replace with actual pet ID -R
        };
        EnteredMedicationName = string.Empty;
        EnteredDosage = string.Empty;

        await _medicationService.SaveMedicationAsync(newMedication);
    }
    public ICommand SaveMedicationCommand => new Command(async () =>
    {
        await SaveMedicationCommandasync();
    });


    /*public async Task LoadMedicationLogsAsync()
    {
        MedicationLogs.Clear();
        var logs = await _medicationService.GetMedicationLogsAsync();
        foreach (var log in logs)
        {
            MedicationLogs.Add(log);
        }
    }*/


}