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
/// optional</b> — owners often know the day and not the slot, and the sheet has a
/// switch for that rather than a picker pre-set to some invented hour. Practice and vet
/// name are free text: there is no directory, no lookup and no entity behind them, the
/// same treatment <c>AppetiteEntry.Food</c> gets.</para>
///
/// <para>The note ("what the vet said") appears only once the visit is <b>past</b>. A
/// note written before the appointment would be a prediction, and this app records
/// rather than predicts.</para>
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

    public VetVisitSheetViewModel(VetVisitService visits, AppointmentReminderScheduler reminders)
    {
        _visits = visits;
        _reminders = reminders;

        SaveCommand = new Command(async () => await SaveAsync());
        DeleteCommand = new Command(async () => await DeleteAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>A visit was written. Carries a confirmation line and <b>no undo</b>:
    /// this sheet IS the editor, so an edit is reversed by opening it again. Deliberately
    /// not a <see cref="JournalSaveResult"/> — that type promises an undo, and the page's
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
    /// visit — the app never fills in a half of the date it was not told.</summary>
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
    public bool CanDelete { get => _canDelete; private set => SetProperty(ref _canDelete, value); }

    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Open for a new visit (<paramref name="visit"/> null) or an existing
    /// one.</summary>
    public Task OpenAsync(int petId, string petName, VetVisit? visit)
    {
        _petId = petId;
        _petName = petName;
        _editing = visit;

        Date = visit?.Date.Date ?? DateTime.Today;
        HasTime = visit?.Time is not null;
        Time = visit?.Time ?? new TimeSpan(9, 0, 0);
        Practice = visit?.Practice ?? string.Empty;
        VetName = visit?.VetName ?? string.Empty;
        VisitNote = visit?.VisitNote ?? string.Empty;
        ShowNote = visit?.IsPast == true;
        CanDelete = visit is not null;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
        return Task.CompletedTask;
    }

    private async Task SaveAsync()
    {
        if (_petId == 0)
            return;

        var visit = _editing ?? new VetVisit { PetId = _petId };
        visit.Date = Date.Date;
        // Off means "I only know the day" — the stored value goes back to null rather
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
        // deliberately re-arms — and the refresh is idempotent, so a visit that is still
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
