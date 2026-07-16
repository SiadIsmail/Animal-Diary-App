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
/// word. No "3/5" anywhere. Appetite is one reading per day (like Mood and Weight):
/// re-logging replaces the day's entry. Save's undo restores the previous reading
/// (or removes it when there was none).
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

        // One reading per day: replace the day's entry if there is one (like Mood /
        // Weight), otherwise insert. Undo restores the previous reading, or removes
        // the row when the day had none.
        var existing = (await _service.GetForDateAsync(_petId, _date)).FirstOrDefault();
        Func<Task> undo;
        if (existing != null)
        {
            var prevLevel = existing.Level;
            var prevTime = existing.Time;
            existing.Level = SelectedLevel;
            existing.Time = DateTime.Now.TimeOfDay;
            await _service.UpdateAsync(existing);
            undo = () =>
            {
                existing.Level = prevLevel;
                existing.Time = prevTime;
                return _service.UpdateAsync(existing);
            };
        }
        else
        {
            var id = await _service.InsertAsync(new AppetiteEntry
            {
                PetId = _petId,
                Date = _date,
                Time = DateTime.Now.TimeOfDay,
                Level = SelectedLevel
            });
            undo = () => _service.DeleteAsync(id);
        }

        var word = ((AppetiteLevel)SelectedLevel).GetDisplayName().ToLowerInvariant();
        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.Format("Journal_ToastAppetite", word),
            undo));
    }
}
