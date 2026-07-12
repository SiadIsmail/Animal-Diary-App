namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>One appetite word-tile: the word plus how full its bowl indicator is.
/// The person sees the word and the bowl; the number is never shown.</summary>
public class AppetiteOption : BaseViewModel
{
    public required int Level { get; init; }
    public required string Word { get; init; }

    /// <summary>Height (px) of the bowl's filled portion — a visual level, no digits.</summary>
    public required double FillHeight { get; init; }

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

/// <summary>
/// Backs the Journal's appetite sheet — five word-tiles (Barely … Everything) with
/// a progressively-filled bowl, in the shared <see cref="Controls.FelovaBottomSheet"/>.
/// Stores the raw level as an <see cref="AppetiteEntry"/>; the timeline shows the
/// word. No "3/5" anywhere. Save's undo removes the just-logged reading.
/// </summary>
public class AppetiteSheetViewModel : BaseViewModel
{
    private const double BowlMaxFill = 22; // px at "Everything"

    private readonly AppetiteEntryService _service;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public AppetiteSheetViewModel(AppetiteEntryService service)
    {
        _service = service;

        for (int level = 1; level <= 5; level++)
        {
            Options.Add(new AppetiteOption
            {
                Level = level,
                Word = ((AppetiteLevel)level).GetDisplayName(),
                FillHeight = level / 5.0 * BowlMaxFill
            });
        }

        SelectCommand = new Command<AppetiteOption>(OnSelect);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    public ObservableCollection<AppetiteOption> Options { get; } = new();

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_AppetiteTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_AppetiteSub", _date);

    private int _selectedLevel;
    public int SelectedLevel
    {
        get => _selectedLevel;
        private set => SetProperty(ref _selectedLevel, value);
    }

    public ICommand SelectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        var today = await _service.GetForDateAsync(petId, _date);
        SetSelected(today.LastOrDefault()?.Level ?? 0);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void OnSelect(AppetiteOption? option)
    {
        if (option != null)
            SetSelected(option.Level);
    }

    private void SetSelected(int level)
    {
        SelectedLevel = level;
        foreach (var o in Options)
            o.IsSelected = o.Level == level;
    }

    private async Task SaveAsync()
    {
        if (SelectedLevel <= 0)
            return;

        var id = await _service.InsertAsync(new AppetiteEntry
        {
            PetId = _petId,
            Date = _date,
            Time = DateTime.Now.TimeOfDay,
            Level = SelectedLevel
        });

        var word = ((AppetiteLevel)SelectedLevel).GetDisplayName().ToLowerInvariant();
        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.Format("Journal_ToastAppetite", word),
            () => _service.DeleteAsync(id)));
    }
}
