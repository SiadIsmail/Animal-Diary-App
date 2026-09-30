namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;

/// <summary>
/// Add or edit one vet visit, in the shared <c>FelovaBottomSheet</c>.
///
/// <para>Four fields and a note, and the shape of them is the design. The <b>time is
/// optional</b>: owners often know the day and not the slot, and the sheet has a
/// switch for that rather than a picker pre-set to some invented hour. Practice and vet
/// name are free text: there is no directory, no lookup and no entity behind them, the
/// same treatment <c>AppetiteEntry.Food</c> gets.</para>
///
/// <para>The note ("what the vet said") appears only once the visit is <b>past</b>. A
/// note written before the appointment would be a prediction, and this app records
/// rather than predicts.</para>
///
/// <para><b>Two ways in, one sheet.</b> <see cref="OpenAsync(int, string, VetVisit?)"/>
/// is the form: four fields and, for a past visit, the note under them.
/// <see cref="OpenForNoteAsync"/> opens straight to the note, with the day, the practice
/// and the vet as a read-only line and "Edit details" for the rare correction. That path
/// exists because the note is the highest-value capture in the whole loop: it is what
/// makes Felova the record of the conversation rather than the owner's half of it, and
/// it decays within hours of leaving the practice. Asking someone to re-confirm four
/// fields they already gave, about a visit that has already happened, before reaching
/// the field that matters is how that note gets lost. Still ONE sheet on the shared
/// FelovaBottomSheet: two would be two places for the note to drift apart.</para>
///
/// <para>Saving re-arms the reminder and deleting cancels it, both here rather than at
/// the caller, so no future entry point can move a visit and leave the old evening's
/// notification armed.</para>
/// </summary>
public class VetVisitSheetViewModel : BaseViewModel
{
    private readonly VetVisitService _visits;
    private readonly AppointmentReminderScheduler _reminders;

    private int _petId;
    private string _petName = string.Empty;
    private VetVisit? _editing;
    private bool _detailsExpanded;

    public VetVisitSheetViewModel(VetVisitService visits, AppointmentReminderScheduler reminders)
    {
        _visits = visits;
        _reminders = reminders;

        SaveCommand = new Command(async () => await SaveAsync());
        DeleteCommand = new Command(async () => await DeleteAsync());
        DismissCommand = new Command(() => IsPresented = false);
        EditDetailsCommand = new Command(ExpandDetails);
    }

    /// <summary>A visit was written. Carries a confirmation line and <b>no undo</b>:
    /// this sheet IS the editor, so an edit is reversed by opening it again. Deliberately
    /// not a <see cref="JournalSaveResult"/>, that type promises an undo, and the page's
    /// toast shows its button whenever one is present.</summary>
    public event Action<string>? Saved;

    /// <summary>A visit was removed, with the undo that brings it back.</summary>
    public event Action<JournalSaveResult>? Deleted;

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => LocalizationManager.Instance.GetString(
        _editing is null ? "Vet_VisitAddTitle"
        : _editing.IsPast ? "Vet_VisitNoteTitle"
        : "Vet_VisitEditTitle");

    public string Subtitle => LocalizationManager.Instance.Format("Vet_VisitSub", _petName);

    private DateTime _date = DateTime.Today;
    public DateTime Date { get => _date; set => SetProperty(ref _date, value); }

    /// <summary>Whether the owner knows the time. Off is the resting state for a new
    /// visit: the app never fills in a half of the date it was not told.</summary>
    private bool _hasTime;
    public bool HasTime { get => _hasTime; set => SetProperty(ref _hasTime, value); }

    private TimeSpan _time = new(9, 0, 0);
    public TimeSpan Time { get => _time; set => SetProperty(ref _time, value); }

    private string _practice = string.Empty;
    public string Practice { get => _practice; set => SetProperty(ref _practice, value); }

    private string _vetName = string.Empty;
    public string VetName { get => _vetName; set => SetProperty(ref _vetName, value); }

    private string _visitNote = string.Empty;
    public string VisitNote { get => _visitNote; set => SetProperty(ref _visitNote, value); }

    /// <summary>The note field, and with it the whole "how did it go?" half of this
    /// sheet, only exists for a visit that has already happened.</summary>
    private bool _showNote;
    public bool ShowNote { get => _showNote; private set => SetProperty(ref _showNote, value); }

    /// <summary>Only an existing visit can be removed.</summary>
    private bool _canDelete;
    public bool CanDelete
    {
        get => _canDelete;
        private set
        {
            if (SetProperty(ref _canDelete, value))
                OnPropertyChanged(nameof(ShowDelete));
        }
    }

    // ── Note-first ───────────────────────────────────────────────────────────

    private bool _noteFirst;

    /// <summary>Opened straight to the note. The four detail fields collapse to one
    /// read-only line until the owner asks for them.</summary>
    public bool NoteFirst { get => _noteFirst; private set => SetProperty(ref _noteFirst, value); }

    /// <summary>Whether the day / time / practice / vet fields render. Always true on the
    /// form path; on the note path only once "Edit details" has been tapped.</summary>
    public bool ShowDetails => !NoteFirst || _detailsExpanded;

    /// <summary>The read-only line, and the "Edit details" link under it. Both retire the
    /// moment the fields appear: the fields say the same thing, editably.</summary>
    public bool ShowDetailsLine => NoteFirst && !_detailsExpanded;

    /// <summary>"Wed, 19 Aug · 15:30 · Dr. Weiss · Riverside": whatever the owner
    /// actually gave. Nothing is filled in for them and nothing reads "unknown".</summary>
    private string _detailsLine = string.Empty;
    public string DetailsLine { get => _detailsLine; private set => SetProperty(ref _detailsLine, value); }

    /// <summary>Room to write. The note is the point of this path, so it opens with the
    /// height a paragraph needs rather than the two lines it gets under a form.</summary>
    public double NoteHeight => NoteFirst ? 210 : 86;

    /// <summary>"Remove this visit" belongs with the details, not under the note. On the
    /// note path the owner is writing down what a vet just told them, and a delete
    /// affordance a thumb-width from that is a hazard rather than an option.</summary>
    public bool ShowDelete => CanDelete && ShowDetails;

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Reveal the four detail fields on the note path.</summary>
    public ICommand EditDetailsCommand { get; }

    /// <summary>Open for a new visit (<paramref name="visit"/> null) or an existing
    /// one.</summary>
    public Task OpenAsync(int petId, string petName, VetVisit? visit) =>
        OpenAsync(petId, petName, visit, noteFirst: false);

    /// <summary>Open straight to "what the vet said", for a visit that has already
    /// happened. Two taps from Today to a saved note. A visit still ahead of us falls
    /// back to the form: there is nothing to write down about it yet.</summary>
    public Task OpenForNoteAsync(int petId, string petName, VetVisit visit) =>
        OpenAsync(petId, petName, visit, noteFirst: visit.IsPast);

    private Task OpenAsync(int petId, string petName, VetVisit? visit, bool noteFirst)
    {
        _petId = petId;
        _petName = petName;
        _editing = visit;
        _detailsExpanded = false;

        Date = visit?.Date.Date ?? DateTime.Today;
        HasTime = visit?.Time is not null;
        Time = visit?.Time ?? new TimeSpan(9, 0, 0);
        Practice = visit?.Practice ?? string.Empty;
        VetName = visit?.VetName ?? string.Empty;
        VisitNote = visit?.VisitNote ?? string.Empty;
        ShowNote = visit?.IsPast == true;
        CanDelete = visit is not null;

        NoteFirst = noteFirst;
        DetailsLine = visit is null ? string.Empty : Details(visit);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ShowDetails));
        OnPropertyChanged(nameof(ShowDetailsLine));
        OnPropertyChanged(nameof(ShowDelete));
        OnPropertyChanged(nameof(NoteHeight));
        IsPresented = true;
        return Task.CompletedTask;
    }

    /// <summary>The rare correction: reveal the four fields in place. One-way: there is
    /// nothing to gain from folding them back up mid-edit.</summary>
    private void ExpandDetails()
    {
        if (_detailsExpanded)
            return;

        _detailsExpanded = true;
        OnPropertyChanged(nameof(ShowDetails));
        OnPropertyChanged(nameof(ShowDetailsLine));
        OnPropertyChanged(nameof(ShowDelete));
    }

    /// <summary>The read-only detail line. Same "whatever the owner gave, nothing
    /// invented" rule the appointment headline follows.</summary>
    private static string Details(VetVisit visit)
    {
        var parts = new List<string>(4)
        {
            visit.Date.ToString("ddd, d MMM", System.Globalization.CultureInfo.CurrentCulture),
        };

        if (visit.Time is TimeSpan t)
            parts.Add(t.ToString("hh\\:mm"));
        if (!string.IsNullOrWhiteSpace(visit.VetName))
            parts.Add(visit.VetName.Trim());
        if (!string.IsNullOrWhiteSpace(visit.Practice))
            parts.Add(visit.Practice.Trim());

        return string.Join(" · ", parts);
    }

    private async Task SaveAsync()
    {
        if (_petId == 0)
            return;

        var visit = _editing ?? new VetVisit { PetId = _petId };
        visit.Date = Date.Date;
        // Off means "I only know the day": the stored value goes back to null rather
        // than keeping whatever the picker last showed.
        visit.Time = HasTime ? Time : null;
        visit.Practice = Practice?.Trim() ?? string.Empty;
        visit.VetName = VetName?.Trim() ?? string.Empty;
        visit.VisitNote = ShowNote ? VisitNote?.Trim() ?? string.Empty : visit.VisitNote;

        await _visits.SaveAsync(visit);

        // Cancel THEN refresh, and cancel unconditionally.
        //
        // A refresh alone only walks the UPCOMING visits, so moving tomorrow's visit
        // into the past dropped it out of the sweep entirely and left its already-armed
        // notification to fire on the eve of a date that no longer exists. Cancelling by
        // id first means the only reminders that survive are the ones the refresh
        // deliberately re-arms, and the refresh is idempotent, so a visit that is still
        // the next one simply gets armed again.
        await _reminders.CancelAsync(visit.Id);
        await _reminders.RefreshAsync();

        IsPresented = false;
        Saved?.Invoke(LocalizationManager.Instance.GetString("Vet_VisitToastSaved"));
    }

    private async Task DeleteAsync()
    {
        if (_editing is null)
            return;

        var id = _editing.Id;
        await _visits.DeleteAsync(id);

        // Cancel by id rather than leaving it to the refresh: the row is a tombstone
        // now, so the sweep can no longer see the visit whose reminder it would drop.
        await _reminders.CancelAsync(id);
        await _reminders.RefreshAsync();

        IsPresented = false;
        Deleted?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Vet_VisitToastDeleted"),
            async () =>
            {
                await _visits.RestoreAsync(id);
                await _reminders.RefreshAsync();
            }));
    }
}
