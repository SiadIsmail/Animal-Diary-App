namespace Animal_Diary_App.Data.ViewModels;

using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Journal's glucose sheet (a <see cref="Controls.FelovaBottomSheet"/>
/// body). New functionality, so it lives in its own ViewModel rather than growing
/// the CalendarViewModel. Opening pre-fills the stepper from the pet's most recent
/// reading and pre-selects Before/After food by time of day; Save writes one
/// <see cref="GlucoseEntry"/> and raises <see cref="Saved"/> so the Journal can
/// toast + refresh.
///
/// The value itself is a precise readout: shown to one decimal, never rounded away.
/// </summary>
public class GlucoseSheetViewModel : BaseViewModel
{
    private readonly GlucoseEntryService _service;

    private int _petId;
    private string _petName = string.Empty;
    private DateTime _date;

    public GlucoseSheetViewModel(GlucoseEntryService service)
    {
        _service = service;

        StepCommand = new Command<string>(OnStep);
        PickContextCommand = new Command<string>(OnPickContext);
        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>Raised after a reading is saved, carrying the confirmation line and
    /// the undo (remove the just-saved reading) for the Journal's toast.</summary>
    public event Action<JournalSaveResult>? Saved;

    // ── Presentation state ─────────────────────────────────────────────────────
    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    public string Title => LocalizationManager.Instance.Format("Journal_GlucoseTitle", _petName);
    public string Subtitle => LocalizationManager.Instance.Format("Journal_GlucoseSub", _date);

    private string _valueText = string.Empty;
    public string ValueText
    {
        get => _valueText;
        set => SetProperty(ref _valueText, value);
    }

    private FoodContext _context = FoodContext.BeforeFood;
    public FoodContext Context
    {
        get => _context;
        private set
        {
            if (SetProperty(ref _context, value))
            {
                OnPropertyChanged(nameof(IsBeforeFood));
                OnPropertyChanged(nameof(IsAfterFood));
            }
        }
    }

    public bool IsBeforeFood => Context == FoodContext.BeforeFood;
    public bool IsAfterFood => Context == FoodContext.AfterFood;

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand StepCommand { get; }
    public ICommand PickContextCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Prepare and present the sheet for a pet + date. Pre-fills the value
    /// from the last reading and pre-selects the food context by time of day.</summary>
    public async Task OpenAsync(int petId, string petName, DateTime date)
    {
        _petId = petId;
        _petName = petName;
        _date = date.Date;

        var recent = await _service.GetMostRecentAsync(petId);
        var start = recent?.Value ?? 7.0m;
        ValueText = start.ToString("0.0", CultureInfo.CurrentCulture);

        // Before food if early morning or late afternoon/evening, else after food —
        // the same gentle rule the prototype uses.
        var hour = DateTime.Now.Hour;
        Context = (hour < 11 || hour >= 16) ? FoodContext.BeforeFood : FoodContext.AfterFood;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void OnStep(string? raw)
    {
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var delta))
            return;

        var current = InputParser.TryParsePositive(ValueText, out var v) ? (double)v : 0;
        var next = Math.Max(0, Math.Round(current + delta, 1));
        ValueText = next.ToString("0.0", CultureInfo.CurrentCulture);
    }

    private void OnPickContext(string? which) =>
        Context = which == "after" ? FoodContext.AfterFood : FoodContext.BeforeFood;

    private async Task SaveAsync()
    {
        if (!InputParser.TryParsePositive(ValueText, out var value) || value <= 0)
            return;

        var id = await _service.InsertAsync(new GlucoseEntry
        {
            PetId = _petId,
            Date = _date,
            Time = DateTime.Now.TimeOfDay,
            Value = value,
            Context = Context
        });

        IsPresented = false;
        Saved?.Invoke(new JournalSaveResult(
            LocalizationManager.Instance.GetString("Journal_ToastGlucose"),
            () => _service.DeleteAsync(id)));
    }
}
