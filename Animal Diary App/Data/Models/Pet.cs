namespace Animal_Diary_App.Data.Models;

using SQLite;
using System.Runtime.CompilerServices;
using System.ComponentModel;

public class Pet : INotifyPropertyChanged, ISyncable
{
    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    /// <summary>LEGACY age-in-years column. Superseded by the birthday fields below —
    /// new code reads <see cref="AgeYears"/>, which derives the age from the birthday.
    /// Still written on save as a snapshot fallback, and still the only age source for
    /// pets created before the birthday system existed. Kept for back-compat (SQLite.NET
    /// never drops columns); don't key new logic off it.</summary>
    public int Age { get; set; }

    /// <summary>Birth year — the one part of a pet's birthday we always require. Many
    /// owners only know roughly when their pet was born, so month and day are optional
    /// (see below). 0 means "no birthday recorded" (only legacy pets).
    /// SQLite.NET adds this column automatically on the next CreateTableAsync.</summary>
    public int BirthYear { get; set; }

    /// <summary>Birth month (1–12) when the owner knows it; null = unknown. We never
    /// fabricate it — an unknown month is stored as null, not "January".</summary>
    public int? BirthMonth { get; set; }

    /// <summary>Birth day of month (1–31) when known; null = unknown. Only meaningful
    /// alongside a known <see cref="BirthMonth"/>. Never fabricated.</summary>
    public int? BirthDay { get; set; }

    /// <summary>The pet's age in completed whole years, or null when nothing is known.
    /// Derived from the birthday using only the parts the owner actually provided:
    /// with a year alone it is the plain year difference (we never pretend an exact
    /// date like January 1st); a known month/day only steps the count back once we can
    /// tell the birthday hasn't come round yet this year. Falls back to the legacy
    /// <see cref="Age"/> column for pets created before the birthday system.</summary>
    [Ignore]
    public int? AgeYears
    {
        get
        {
            if (BirthYear > 0)
            {
                var today = DateTime.Today;
                int age = today.Year - BirthYear;

                // Step back a year only when we KNOW the birthday is still ahead this
                // year. Unknown finer parts are treated as already-passed, so a
                // year-only birthday reads as the raw year difference.
                if (BirthMonth is int m)
                {
                    if (today.Month < m)
                        age--;
                    else if (today.Month == m && BirthDay is int d && today.Day < d)
                        age--;
                }

                return age < 0 ? 0 : age;
            }

            // Legacy pets stored a plain age with no birthday.
            return Age > 0 ? Age : (int?)null;
        }
    }

    /// <summary>File name (relative to <see cref="Animal_Diary_App.Data.Services.PetPhotoService.PhotosDirectory"/>)
    /// of the pet's profile photo, or null/empty when it has none. Stored relative —
    /// never an absolute path — because the app-data root can move between installs
    /// (same reason the report library stores relative names). SQLite.NET adds this
    /// column automatically. NOTE: this column syncs, but the image file itself does
    /// NOT — on another device the file is absent, so every avatar surface falls back
    /// to the type emoji (see <see cref="PhotoFullPath"/> and PetAvatarView).</summary>
    public string? PhotoFileName { get; set; }

    /// <summary>Absolute path of the profile photo, or null when there is none set.
    /// UI-agnostic (just a path string); callers still guard on File.Exists because a
    /// synced-in row can name a file this device doesn't have.</summary>
    [Ignore]
    public string? PhotoFullPath => string.IsNullOrEmpty(PhotoFileName)
        ? null
        : System.IO.Path.Combine(Animal_Diary_App.Data.Services.PetPhotoService.PhotosDirectory, PhotoFileName);

    /// <summary>Id of the pet's ongoing condition (see ConditionCatalog); empty =
    /// "None / Not sure". Chosen in the condition picker after the pet is created
    /// and drives which tracking items the Calendar shows. SQLite.NET adds this
    /// column automatically on the next CreateTableAsync — no manual migration.</summary>
    public string ConditionId { get; set; } = string.Empty;

    /// <summary>Localized age label, e.g. "(3 yrs)" / "(3 J.)". Used by calendar chips.
    /// Empty when the age is unknown (no birthday and no legacy age).</summary>
    [Ignore]
    public string AgeDisplay => AgeYears is int years
        ? Animal_Diary_App.Helpers.LocalizationManager.Instance.Format("Common_AgeYearsShort", years)
        : string.Empty;

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
public class PetEntry : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

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

