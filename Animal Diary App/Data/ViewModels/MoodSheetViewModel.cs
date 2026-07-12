namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's mood sheet (the ported 5-emoji picker + optional note),
/// rendered in the shared <see cref="Controls.FelovaBottomSheet"/>. Persists to the
/// day's <see cref="PetEntry"/> like the old inline editor did; Save raises
/// <see cref="Saved"/> with an undo that restores the previous mood.
/// </summary>
public class MoodSheetViewModel : BaseViewModel
{
    private readonly PetEntryService _petEntries;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public MoodSheetViewModel(PetEntryService petEntries)
    {
        _petEntries = petEntries;

        SelectCommand = new Command<string>(v => { if (int.TryParse(v, out var l)) Level = l; });
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.Format("Journal_MoodSheetTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_MoodSheetSub", _date);

    private int _level; // 0 = none, 1..5
    public int Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    private string _noteText = string.Empty;
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }

    public ICommand SelectCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        var existing = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, petId);
        Level = existing?.MoodLevel ?? 0;
        NoteText = existing?.MoodNote ?? string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private async Task SaveAsync()
    {
        if (Level <= 0)
            return;

        // Capture the previous mood so undo can put it back exactly.
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        var prevLevel = entry?.MoodLevel ?? 0;
        var prevMood = entry?.Mood ?? string.Empty;
        var prevNote = entry?.MoodNote ?? string.Empty;

        var name = ((MoodLevel)Level).GetDisplayName();
        var note = NoteText?.Trim() ?? string.Empty;
        await WriteMoodAsync(Level, name, note);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.Format("Toast_MoodSaved1", _petName),
            () => WriteMoodAsync(prevLevel, prevMood, prevNote)));
    }

    // Write just the mood columns of the day's entry, leaving weight untouched.
    private async Task WriteMoodAsync(int level, string moodName, string note)
    {
        var entry = await _petEntries.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        if (entry != null)
        {
            entry.MoodLevel = level;
            entry.Mood = moodName;
            entry.MoodNote = note;
            await _petEntries.UpdatePetEntryAsync(entry);
        }
        else
        {
            await _petEntries.SavePetEntryAsync(new PetEntry
            {
                PetId = _petId,
                Date = _date,
                MoodLevel = level,
                Mood = moodName,
                MoodNote = note
            });
        }
    }
}
