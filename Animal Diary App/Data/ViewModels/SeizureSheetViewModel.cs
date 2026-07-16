namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's seizure sheet — a time picker plus two optional fields
/// (duration in minutes and a note), in the shared
/// <see cref="Controls.FelovaBottomSheet"/>. Seizures are an Event tracker: logged
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

    private string _durationText = string.Empty;
    public string DurationText { get => _durationText; set => SetProperty(ref _durationText, value); }

    private string _noteText = string.Empty;
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }

    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        // Seizures are logged as they happen, so default to now and start blank.
        Time = DateTime.Now.TimeOfDay;
        DurationText = string.Empty;
        NoteText = string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        // Both fields are optional — a seizure is worth logging even with just a time.
        int? duration = int.TryParse(DurationText?.Trim(), out var minutes) && minutes > 0
            ? minutes
            : null;

        var id = await _service.InsertAsync(new SeizureEntry
        {
            PetId = _petId,
            Date = _date,
            Time = Time,
            DurationMinutes = duration,
            Note = NoteText?.Trim() ?? string.Empty
        });

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Journal_ToastSeizure"),
            () => _service.DeleteAsync(id)));
    }
}
