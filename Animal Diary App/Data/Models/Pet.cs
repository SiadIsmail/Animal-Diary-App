namespace Animal_Diary_App.Data.Models;

using SQLite;
using System.Runtime.CompilerServices;
using System.ComponentModel;

public class Pet : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Age { get; set; }

    /// <summary>Id of the pet's ongoing condition (see ConditionCatalog); empty =
    /// "None / Not sure". Chosen in the condition picker after the pet is created
    /// and drives which tracking items the Calendar shows. SQLite.NET adds this
    /// column automatically on the next CreateTableAsync — no manual migration.</summary>
    public string ConditionId { get; set; } = string.Empty;

    /// <summary>Localized age label, e.g. "(3 yrs)" / "(3 J.)". Used by calendar chips.</summary>
    [Ignore]
    public string AgeDisplay => Animal_Diary_App.Helpers.LocalizationManager.Instance.Format("Common_AgeYearsShort", Age);

    /// <summary>The pet's care plan — the trackers the Journal asks for. Not a column;
    /// <see cref="Tracker"/> rows are stored separately and loaded into this list by
    /// the care-plan service. New pets are seeded from <see cref="CarePlanCatalog"/>.</summary>
    [Ignore]
    public List<Tracker> CarePlan { get; set; } = new();
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }
}
public class PetEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Composite index on (PetId, Date): every entry query filters by pet and a
    // date range, so this covers the weight-chart, mood-timeline and day-lookup
    // reads with a single B-tree instead of a full table scan.
    [Indexed(Name = "IX_PetEntry_Pet_Date", Order = 1)]
    public int PetId { get; set; }
    [Indexed(Name = "IX_PetEntry_Pet_Date", Order = 2)]
    public DateTime Date { get; set; }
    public string Mood { get; set; } = string.Empty;
    public int MoodLevel { get; set; } = 0;

    /// <summary>Optional free-text note the owner writes alongside the mood (the
    /// journal's washi-tape card shows it). Added when Notes was folded into the
    /// Mood tracker. SQLite.NET adds this column automatically — no migration.</summary>
    public string MoodNote { get; set; } = string.Empty;

    /// <summary>Whether the owner asked for this day's <see cref="MoodNote"/> to
    /// appear in the vet report's Owner's Notes section. Defaults to false, so
    /// notes are private to the app unless the owner opts in per note; legacy
    /// entries written before this column read as false. SQLite.NET adds the
    /// column automatically — no migration.</summary>
    public bool IncludeInVetReport { get; set; }
    public decimal Weight { get; set; }

    /// <summary>Time-of-day (ticks) the mood was logged, or null for entries written
    /// before per-entry times existed. Lets the chronological Journal timeline place
    /// mood at the moment it was recorded. SQLite.NET adds this column automatically.</summary>
    public long? MoodTimeTicks { get; set; }

    /// <summary>Time-of-day (ticks) the weight was logged, or null for legacy entries.
    /// Weight and mood carry separate times because they're logged independently.</summary>
    public long? WeightTimeTicks { get; set; }
}

