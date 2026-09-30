namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One seizure-type tile: the vet's term, and whether it's the chosen one.
/// Mirrors <see cref="AppetiteOption"/>.</summary>
public class SeizureTypeOption : BaseViewModel
{
    public required SeizureType Type { get; init; }

    /// <summary>The localized term, resolved LIVE on every read, never cached. The sheet
    /// VM is a singleton, so a cached word would freeze in the language active at
    /// construction (see coding-standards).</summary>
    public string Word => Type.GetDisplayName();

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-raise <see cref="Word"/> after a language switch.</summary>
    public void RefreshWord() => OnPropertyChanged(nameof(Word));
}

/// <summary>
/// Backs the Journal's seizure sheet: a time picker plus three optional fields
/// (type, duration and a note), in the shared
/// <c>FelovaBottomSheet</c>. Seizures are an Event tracker: logged
/// as they happen from the "+" sheet, one <see cref="SeizureEntry"/> per occurrence
/// (never upserted). Mirrors <see cref="AppetiteSheetViewModel"/>; Save's undo
/// removes the just-logged occurrence.
///
/// <para><b>Duration is stored in SECONDS and the sheet opens on seconds</b>, because
/// that is how owners describe seizures: "about forty seconds", never "about 0.7
/// minutes". It used to be an int of whole minutes, which could not hold a 45-second
/// event at all, and sub-minute seizures are both common and clinically relevant, so
/// that was data loss rather than a rough edge. Minutes stay on offer for the long
/// ones.</para>
/// </summary>
public class SeizureSheetViewModel : BaseViewModel
{
    private readonly SeizureEntryService _service;
    private readonly DisplayUnitService _units;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public SeizureSheetViewModel(SeizureEntryService service, DisplayUnitService units)
    {
        _service = service;
        _units = units;

        foreach (var type in Enum.GetValues<SeizureType>())
            TypeOptions.Add(new SeizureTypeOption { Type = type });

        foreach (var unit in UnitCatalog.ForFamily(UnitFamily.Duration))
            Units.Add(new UnitOption(unit));

        // Live language switch: re-resolve every tile's term (they're never cached).
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var o in TypeOptions)
                o.RefreshWord();
            foreach (var u in Units)
                u.RefreshLocalized();
            OnPropertyChanged(nameof(DurationLabel));
        };

        SelectTypeCommand = new Command<SeizureTypeOption>(OnSelectType);
        SelectUnitCommand = new Command<UnitOption>(OnSelectUnit);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_SeizureTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_SeizureSub", _date);

    private TimeSpan _time;
    public TimeSpan Time { get => _time; set => SetProperty(ref _time, value); }

    public ObservableCollection<SeizureTypeOption> TypeOptions { get; } = new();

    /// <summary>The chosen type, or null when the owner didn't say. Null is the resting
    /// state: nothing is pre-selected, and re-tapping the chosen tile clears it.</summary>
    private SeizureType? _selectedType;
    public SeizureType? SelectedType
    {
        get => _selectedType;
        private set => SetProperty(ref _selectedType, value);
    }

    private string _durationText = string.Empty;
    public string DurationText { get => _durationText; set => SetProperty(ref _durationText, value); }

    private string _noteText = string.Empty;
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }

    // ── The unit the duration is typed in ────────────────────────────────────

    /// <summary>Seconds · minutes, in catalog order, which puts the canonical unit
    /// first: that is also the one this sheet wants to open on.</summary>
    public ObservableCollection<UnitOption> Units { get; } = new();

    private UnitDef _unit = UnitCatalog.Canonical(UnitFamily.Duration);

    /// <summary>"Duration · optional · sec": the caption carries the unit, so the field
    /// is never a bare number whose meaning lives in a chip somewhere else.</summary>
    public string DurationLabel =>
        LocalizationManager.Instance.Format("Journal_SeizureDurationLabelUnit", _unit.Label);

    public ICommand SelectTypeCommand { get; }
    public ICommand SelectUnitCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        // Seizures are an event store, so opening always means "log another", never
        // "edit the last": the sheet opens in whatever this owner's own entries resolved
        // to, which for a household that has never timed one is seconds.
        ApplyUnit(await _units.ResolveAsync(petId, UnitFamily.Duration));

        // Seizures are logged as they happen, so default to now and start blank,
        // including the type, which is never pre-selected from a previous entry.
        Time = DateTime.Now.TimeOfDay;
        SetSelectedType(null);
        DurationText = string.Empty;
        NoteText = string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    // Tapping the chosen tile again clears it. Without that, a mis-tap could only be
    // corrected by choosing a different (wrong) answer, and "I don't actually know"
    // has to stay reachable after the first tap, not just before it.
    private void OnSelectType(SeizureTypeOption? option)
    {
        if (option == null)
            return;
        SetSelectedType(SelectedType == option.Type ? null : option.Type);
    }

    // One pass sets the chosen tile and clears every other (coding-standards: never
    // "clear the old one, set the new one" in two places).
    private void SetSelectedType(SeizureType? type)
    {
        SelectedType = type;
        foreach (var o in TypeOptions)
            o.IsSelected = type is SeizureType t && o.Type == t;
    }

    /// <summary>Point the sheet at a unit: the chips and the caption follow from it.</summary>
    private void ApplyUnit(UnitDef unit)
    {
        _unit = unit;

        // One pass that sets one and clears every other, matched by id.
        foreach (var option in Units)
            option.IsSelected = option.Unit.Id == unit.Id;

        OnPropertyChanged(nameof(DurationLabel));
    }

    /// <summary>Switching unit re-expresses what is already typed: 90 seconds becomes
    /// 1.5 minutes, never "90 minutes".</summary>
    private void OnSelectUnit(UnitOption? option)
    {
        if (option is null || option.Unit.Id == _unit.Id)
            return;

        var canonical = InputParser.TryParsePositive(DurationText, out var typed)
            ? UnitCatalog.ToCanonicalValue(typed, _unit)
            : (decimal?)null;

        ApplyUnit(option.Unit);

        if (canonical is decimal value)
            DurationText = UnitCatalog.Format(value, option.Unit);
    }

    private async Task SaveAsync()
    {
        // Every field but the time is optional: a seizure is worth logging even with
        // just a time, and an unanswered type stays null rather than becoming a guess.
        //
        // Stored in SECONDS whatever was typed, rounded to a whole one: half a second is
        // below the resolution of anyone timing a seizure by hand, and 1.5 minutes has to
        // land on exactly 90 rather than on 89.999...
        int? duration = null;
        if (InputParser.TryParsePositive(DurationText, out var typed) && typed > 0)
        {
            var seconds = decimal.Round(
                UnitCatalog.ToCanonicalValue(typed, _unit), 0, MidpointRounding.AwayFromZero);
            if (seconds > 0)
                duration = (int)seconds;
        }

        var id = await _service.InsertAsync(new SeizureEntry
        {
            PetId = _petId,
            Date = _date,
            Time = Time,
            Type = SelectedType,
            DurationSeconds = duration,
            // Provenance only when there is a duration to have provenance about: a unit
            // recorded against a blank field would vote in the majority for a reading
            // nobody took.
            Unit = duration is null ? null : _unit.Id,
            Note = NoteText?.Trim() ?? string.Empty
        });

        // Remembered on SAVE, and only when a duration was actually given: a chip tapped
        // and then left blank is not a preference.
        if (duration is not null)
            await _units.RememberAsync(_unit);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Journal_ToastSeizure"),
            () => _service.DeleteAsync(id)));
    }
}
