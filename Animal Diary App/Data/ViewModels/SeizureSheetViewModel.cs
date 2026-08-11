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

    /// <summary>The localized term, resolved LIVE on every read — never cached. The sheet
    /// VM is a singleton, so a cached word would freeze in the language active at
    /// construction (see coding-standards).</summary>
    public string Word => Type.GetDisplayName();

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    /// <summary>Re-raise <see cref="Word"/> after a language switch.</summary>
    public void RefreshWord() => OnPropertyChanged(nameof(Word));
}

/// <summary>
/// Backs the Journal's seizure sheet — a time picker plus three optional fields
/// (type, duration in minutes and a note), in the shared
/// <c>FelovaBottomSheet</c>. Seizures are an Event tracker: logged
/// as they happen from the "+" sheet, one <see cref="SeizureEntry"/> per occurrence
/// (never upserted). Mirrors <see cref="AppetiteSheetViewModel"/>; Save's undo
/// removes the just-logged occurrence.
/// </summary>
public class SeizureSheetViewModel : BaseViewModel
{
    private readonly SeizureEntryService _service;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public SeizureSheetViewModel(SeizureEntryService service)
    {
        _service = service;

        foreach (var type in Enum.GetValues<SeizureType>())
            TypeOptions.Add(new SeizureTypeOption { Type = type });

        // Live language switch: re-resolve every tile's term (they're never cached).
        LocalizationManager.Instance.PropertyChanged += (_, _) =>
        {
            foreach (var o in TypeOptions)
                o.RefreshWord();
        };

        SelectTypeCommand = new Command<SeizureTypeOption>(OnSelectType);
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

    public ICommand SelectTypeCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        // Seizures are logged as they happen, so default to now and start blank —
        // including the type, which is never pre-selected from a previous entry.
        Time = DateTime.Now.TimeOfDay;
        SetSelectedType(null);
        DurationText = string.Empty;
        NoteText = string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
        return Task.CompletedTask;
    }

    // Tapping the chosen tile again clears it. Without that, a mis-tap could only be
    // corrected by choosing a different (wrong) answer — and "I don't actually know"
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

    private async Task SaveAsync()
    {
        // Every field but the time is optional — a seizure is worth logging even with
        // just a time, and an unanswered type stays null rather than becoming a guess.
        int? duration = int.TryParse(DurationText?.Trim(), out var minutes) && minutes > 0
            ? minutes
            : null;

        var id = await _service.InsertAsync(new SeizureEntry
        {
            PetId = _petId,
            Date = _date,
            Time = Time,
            Type = SelectedType,
            DurationMinutes = duration,
            Note = NoteText?.Trim() ?? string.Empty
        });

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Journal_ToastSeizure"),
            () => _service.DeleteAsync(id)));
    }
}
