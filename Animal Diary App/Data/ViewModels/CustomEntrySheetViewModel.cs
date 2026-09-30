namespace Animal_Diary_App.Data.ViewModels;

using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's sheet for a tracker the owner defined: a time, an optional
/// number (only when the tracker records one) and an optional note, in the shared
/// <c>FelovaBottomSheet</c>.
///
/// <para><b>This is the one and only logging sheet for every custom tracker.</b> Its
/// title is the owner's own name for the thing, so the sheet reads as "Walk" or
/// "Ohrentropfen" without a line of code knowing either word. Mirrors
/// <see cref="SeizureSheetViewModel"/>: custom entries are events, so Save inserts one
/// <see cref="CustomEntry"/> and its undo removes exactly that one.</para>
/// </summary>
public class CustomEntrySheetViewModel : BaseViewModel
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private readonly CustomTrackerService _service;

    private int _petId;
    private DateTime _date;
    private CustomTracker? _tracker;

    public CustomEntrySheetViewModel(CustomTrackerService service)
    {
        _service = service;

        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    public event Action<JournalSaveResult>? Saved;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>The owner's own name for this tracker: user text, shown verbatim and
    /// never passed through the localizer.</summary>
    public string Title => _tracker?.Name ?? string.Empty;

    public string Subtitle => Loc.Format("Journal_CustomSub", _date);

    private TimeSpan _time;
    public TimeSpan Time { get => _time; set => SetProperty(ref _time, value); }

    /// <summary>Whether this tracker records a number at all. A Tick tracker's sheet is
    /// just a time and a note: there is nothing to type, and asking for one would be
    /// asking for a number the owner never said existed.</summary>
    public bool ShowAmount => _tracker?.Shape == CustomShape.Amount;

    /// <summary>The owner's own unit ("min", "bowls"), or empty. Shown beside the field
    /// rather than folded into a sentence: it is their word, in their language.</summary>
    public string Unit => _tracker?.Unit ?? string.Empty;
    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);

    private string _amountText = string.Empty;
    public string AmountText { get => _amountText; set => SetProperty(ref _amountText, value); }

    private string _noteText = string.Empty;
    public string NoteText { get => _noteText; set => SetProperty(ref _noteText, value); }

    /// <summary>Open the sheet for one of the pet's own trackers.
    ///
    /// <para>Reads the definition fresh rather than taking it from the caller: the chip
    /// that opened this may have been built before the owner renamed or reshaped the
    /// tracker on the Manage page, and the sheet must never show a stale unit next to a
    /// field that writes a real value.</para></summary>
    public async Task OpenAsync(int petId, DateTime date, int customTrackerId)
    {
        _petId = petId;
        _date = date.Date;
        _tracker = await _service.GetByIdAsync(customTrackerId);

        // A tracker deleted from another device between the chip being drawn and tapped.
        // Nothing to log against, so open nothing rather than a nameless sheet.
        if (_tracker == null)
            return;

        // Events are written down as they happen, so default to now and start blank.
        Time = DateTime.Now.TimeOfDay;
        AmountText = string.Empty;
        NoteText = string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ShowAmount));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(HasUnit));
        IsPresented = true;
    }

    private async Task SaveAsync()
    {
        if (_tracker == null)
            return;

        // Every field is optional: including the number. "It happened" is the whole
        // point of a Tick tracker, and it is still worth writing down for an Amount one
        // when nobody counted. InputParser handles both decimal separators.
        decimal? amount = null;
        if (ShowAmount && InputParser.TryParsePositive(AmountText?.Trim() ?? string.Empty, out var parsed))
            amount = parsed;

        var id = await _service.InsertAsync(new CustomEntry
        {
            PetId = _petId,
            CustomTrackerId = _tracker.Id,
            Date = _date,
            Time = Time,
            Amount = amount,
            // The definition's unit AS IT STANDS NOW, copied onto the row. Renaming the
            // tracker's unit later ("min" -> "km") must not restate 35 minutes of walking
            // as 35 kilometres: history is written at the moment it happened. Same
            // reasoning as MedicationChange's stored name and summary. Only when there is
            // a number for it to describe.
            Unit = amount is null ? null : _tracker.Unit?.Trim(),
            Note = NoteText?.Trim() ?? string.Empty,
        });

        // The tracker's own name carries the confirmation, through a template rather than
        // concatenation: word order differs by language (AI/app-voice.md §20).
        var message = Loc.Format("Journal_ToastCustom", _tracker.Name);

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(message, () => _service.DeleteEntryAsync(id)));
    }

    /// <summary>The value line a timeline card shows for one entry: the number with the
    /// owner's unit, the note, or both. Lives here beside the sheet that writes them so
    /// the two can't word the same entry differently.</summary>
    public static string Describe(CustomEntry entry, CustomTracker? tracker)
    {
        var parts = new List<string>(2);

        if (entry.Amount is decimal value)
        {
            var number = value.ToString("0.#", CultureInfo.CurrentCulture);
            // The unit the ENTRY was written in, not the definition's current one.
            var unit = entry.UnitFor(tracker);
            parts.Add(unit.Length > 0 ? $"{number} {unit}" : number);
        }

        var note = entry.Note?.Trim() ?? string.Empty;
        if (note.Length > 0)
            parts.Add(note);

        return string.Join(" · ", parts);
    }
}
