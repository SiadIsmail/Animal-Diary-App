namespace Animal_Diary_App.Data.Models;

using SQLite;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class Medication : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PetId { get; set; }

    private string name = string.Empty;
    public string Name
    {
        get => name;
        set
        {
            if (name != value)
            {
                name = value;
                OnPropertyChanged();
            }
        }
    }

    private decimal dosage;
    public decimal Dosage
    {
        get => dosage;
        set
        {
            if (dosage != value)
            {
                dosage = value;
                OnPropertyChanged();
            }
        }
    }

    private string notes = string.Empty;
    public string Notes
    {
        get => notes;
        set
        {
            if (notes != value)
            {
                notes = value;
                OnPropertyChanged();
            }
        }
    }

    private string unit = "mg";
    public string Unit
    {
        get => unit;
        set
        {
            if (unit != value)
            {
                unit = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsArchived { get; set; } = false;

    /// <summary>
    /// When the medication was created. Bounds the missed-dose reconciliation so
    /// it never fabricates "missed" entries for dates before the medication
    /// existed. Pre-existing rows default to <see cref="DateTime.MinValue"/>.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

public class MedicationSchedule
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    [Indexed]
    public int MedicationId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeSpan Time { get; set; }


}

public class FilteredMedication
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string DoseDisplay { get; set; } = string.Empty;
    public string FrequencyDisplay { get; set; } = string.Empty;

    /// <summary>All reminder times for the day, e.g. "08:00 · 20:00".</summary>
    public string TimesDisplay { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    /// <summary>Whether this medication has an optional note worth showing.</summary>
    public bool HasFoodNote => !string.IsNullOrWhiteSpace(Note);
}
