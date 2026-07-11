namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Helpers;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;
using System.Windows.Input;

// ─────────────────────────────────────────────────────────────────────────────
//  The Tracker Hub model.
//
//  Everything the owner can log — Mood, Weight, Appetite, a medication dose, a
//  blood-glucose reading — is a "Tracker" surfaced through ONE progressive-
//  disclosure flow: a short chooser list (root) → optional group drill-in →
//  a single tracker's input. This file defines the pieces:
//
//    TrackerLeaf     abstract editor for one loggable value. Subclasses differ
//                    only in how they render + where they persist:
//                      MoodTrackerLeaf    → PetEntry (emoji + note)
//                      WeightTrackerLeaf  → PetEntry (kg)
//                      DynamicTrackerLeaf → TrackingEntry (any InputKind)
//    TrackerGroup    a drill-in row (Medications, or a condition like Diabetes).
//    TrackerRow      one chooser row wrapping either a leaf or a group.
//
//  A leaf is built fully hydrated with the day's current value, so its chooser
//  row can show a ✓ summary without opening it. Chip-style kinds (Scale / Boolean
//  / Dose) save on tap; typed kinds (Numeric / Text / Event / Mood) save via a
//  button. Saving raises Saved, which the CalendarViewModel uses to collapse the
//  input and refresh summaries.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One loggable value in the Tracker Hub. Base class holds the surface
/// the chooser + input templates bind to; subclasses supply rendering kind and
/// persistence.</summary>
public abstract class TrackerLeaf : BaseViewModel
{
    protected TrackerLeaf()
    {
        SaveCommand = new Command(async () => await SaveAsync());
    }

    /// <summary>Raised after the value is persisted (so the hub can collapse + refresh).</summary>
    public event Action<TrackerLeaf>? Saved;

    public abstract string Name { get; }
    public abstract string Icon { get; }
    public virtual string Unit => string.Empty;
    public bool HasUnit => !string.IsNullOrEmpty(Unit);

    /// <summary>Drives both the input template and the template selector.</summary>
    public abstract InputKind TemplateKind { get; }

    /// <summary>True when something is logged for this tracker today.</summary>
    public abstract bool HasValue { get; }

    /// <summary>Short collapsed-state summary, e.g. "148 mg/dL" or "😊 Good".</summary>
    public abstract string Summary { get; }

    // Numeric / Volume / Weight all bind their entry to this shared field.
    private string _valueText = string.Empty;
    public string ValueText
    {
        get => _valueText;
        set { if (SetProperty(ref _valueText, value)) NotifyState(); }
    }

    /// <summary>Commits the current value. Bound by typed-input templates and
    /// invoked by chip taps in subclasses.</summary>
    public ICommand SaveCommand { get; }

    protected abstract Task PersistAsync();

    protected async Task SaveAsync()
    {
        await PersistAsync();
        NotifyState();
        Saved?.Invoke(this);
    }

    /// <summary>Re-raise the derived display state after an edit.</summary>
    protected void NotifyState()
    {
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>Mood: the bespoke 5-emoji picker plus an optional free-text note,
/// saved together to the day's PetEntry.</summary>
public class MoodTrackerLeaf : TrackerLeaf
{
    private readonly PetEntryService _petEntryService;
    private readonly int _petId;
    private readonly DateTime _date;

    public MoodTrackerLeaf(int petId, DateTime date, PetEntryService petEntryService, PetEntry? existing)
    {
        _petId = petId;
        _date = date.Date;
        _petEntryService = petEntryService;

        SelectMoodCommand = new Command<string>(v =>
        {
            if (int.TryParse(v, out var level))
                MoodLevelValue = level;
        });

        if (existing != null)
        {
            _moodLevelValue = existing.MoodLevel;
            _moodNoteText = existing.MoodNote ?? string.Empty;
        }
    }

    /// <summary>Selecting an emoji only sets the level — the note-aware Save button
    /// commits, so the owner can pick a face AND jot a note in one step.</summary>
    public ICommand SelectMoodCommand { get; }

    public override string Name => "Mood";
    public override string Icon => "🙂";
    public override InputKind TemplateKind => InputKind.Mood;

    private int _moodLevelValue; // 0 = none, else 1..5
    public int MoodLevelValue
    {
        get => _moodLevelValue;
        set { if (SetProperty(ref _moodLevelValue, value)) NotifyState(); }
    }

    private string _moodNoteText = string.Empty;
    public string MoodNoteText
    {
        get => _moodNoteText;
        set => SetProperty(ref _moodNoteText, value);
    }

    public override bool HasValue => MoodLevelValue > 0;

    public override string Summary =>
        HasValue
            ? $"{((MoodLevel)MoodLevelValue).GetEmoji()} {((MoodLevel)MoodLevelValue).GetDisplayName()}"
            : string.Empty;

    protected override async Task PersistAsync()
    {
        if (MoodLevelValue <= 0)
            return;

        // Re-read so we only touch the mood columns and don't clobber the weight
        // the WeightTrackerLeaf may have written to the same PetEntry row.
        var entry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        var name = ((MoodLevel)MoodLevelValue).GetDisplayName();
        var note = MoodNoteText?.Trim() ?? string.Empty;

        if (entry != null)
        {
            entry.MoodLevel = MoodLevelValue;
            entry.Mood = name;
            entry.MoodNote = note;
            await _petEntryService.UpdatePetEntryAsync(entry);
        }
        else
        {
            await _petEntryService.SavePetEntryAsync(new PetEntry
            {
                PetId = _petId,
                Date = _date,
                MoodLevel = MoodLevelValue,
                Mood = name,
                MoodNote = note
            });
        }
    }
}

/// <summary>Weight: a single kg number, saved to the day's PetEntry (so the Today
/// page's weight chart keeps working).</summary>
public class WeightTrackerLeaf : TrackerLeaf
{
    private readonly PetEntryService _petEntryService;
    private readonly int _petId;
    private readonly DateTime _date;

    public WeightTrackerLeaf(int petId, DateTime date, PetEntryService petEntryService, PetEntry? existing)
    {
        _petId = petId;
        _date = date.Date;
        _petEntryService = petEntryService;

        if (existing != null && existing.Weight > 0)
            ValueText = existing.Weight.ToString();
    }

    public override string Name => "Weight";
    public override string Icon => "⚖️";
    public override string Unit => LocalizationManager.Instance.GetString("Common_KgSuffix").Trim();
    public override InputKind TemplateKind => InputKind.Numeric;

    public override bool HasValue => InputParser.TryParsePositive(ValueText, out _);

    public override string Summary =>
        HasValue ? $"{ValueText} {Unit}".Trim() : string.Empty;

    protected override async Task PersistAsync()
    {
        if (!InputParser.TryParsePositive(ValueText, out var weight))
            return;

        var entry = await _petEntryService.GetPetEntryByDateAndPetIdAsync(_date, _petId);
        if (entry != null)
        {
            entry.Weight = weight;
            await _petEntryService.UpdatePetEntryAsync(entry);
        }
        else
        {
            await _petEntryService.SavePetEntryAsync(new PetEntry
            {
                PetId = _petId,
                Date = _date,
                Weight = weight
            });
        }
    }
}

/// <summary>Any catalog <see cref="TrackingItem"/> — glucose, water, seizure,
/// appetite… — persisted to the generic <see cref="TrackingEntry"/> table. Holds
/// the edit state for every InputKind; only the fields the item's kind uses are
/// touched.</summary>
public class DynamicTrackerLeaf : TrackerLeaf
{
    private readonly TrackingEntryService _service;
    private readonly int _petId;
    private readonly DateTime _date;
    private int _rowId; // 0 = insert, >0 = update
    private long? _timeTicks; // saved time-of-day for Dose / Event

    public TrackingItem Item { get; }

    public DynamicTrackerLeaf(
        TrackingItem item, int petId, DateTime date,
        TrackingEntryService service, TrackingEntry? existing)
    {
        Item = item;
        _petId = petId;
        _date = date.Date;
        _service = service;

        SelectScaleCommand = new Command<string>(async v => await SetScaleAsync(v));
        SelectSeverityCommand = new Command<string>(SetSeverity);
        SetBoolCommand = new Command<string>(async v => await SetBoolAsync(v));
        SetDoseCommand = new Command<string>(async v => await SetDoseAsync(v));

        Hydrate(existing);
    }

    public override string Name => Item.Name;
    public override string Icon => Item.Icon;
    public override string Unit => Item.Unit;
    public override InputKind TemplateKind => Item.Kind;

    // ── Edit state (only the fields relevant to the kind are used) ────────
    private string _noteText = string.Empty;
    public string NoteText
    {
        get => _noteText;
        set { if (SetProperty(ref _noteText, value)) NotifyState(); }
    }

    private int _scaleValue; // 0 = unset, else 1..ScaleMax
    public int ScaleValue
    {
        get => _scaleValue;
        private set { if (SetProperty(ref _scaleValue, value)) NotifyState(); }
    }

    private int _boolState; // 0 unset, 1 yes, 2 no
    public int BoolState
    {
        get => _boolState;
        private set { if (SetProperty(ref _boolState, value)) NotifyState(); }
    }

    private int _doseState; // 0 unset, 1 given, 2 skipped
    public int DoseState
    {
        get => _doseState;
        private set { if (SetProperty(ref _doseState, value)) NotifyState(); }
    }

    private int _severityValue; // 0 unset, else 1..5
    public int SeverityValue
    {
        get => _severityValue;
        private set { if (SetProperty(ref _severityValue, value)) NotifyState(); }
    }

    private string _durationText = string.Empty;
    public string DurationText
    {
        get => _durationText;
        set { if (SetProperty(ref _durationText, value)) NotifyState(); }
    }

    private string _eventNote = string.Empty;
    public string EventNote
    {
        get => _eventNote;
        set { if (SetProperty(ref _eventNote, value)) NotifyState(); }
    }

    // ── Chip commands (bound by the scale / boolean / dose / event templates) ──
    public ICommand SelectScaleCommand { get; }
    public ICommand SelectSeverityCommand { get; }
    public ICommand SetBoolCommand { get; }
    public ICommand SetDoseCommand { get; }

    public override bool HasValue => Item.Kind switch
    {
        InputKind.Numeric or InputKind.Volume => TryNumber(out _),
        InputKind.Scale => ScaleValue > 0,
        InputKind.Boolean => BoolState > 0,
        InputKind.Text => !string.IsNullOrWhiteSpace(NoteText),
        InputKind.Dose => DoseState > 0,
        InputKind.Event => SeverityValue > 0 || !string.IsNullOrWhiteSpace(EventNote) || ParsedDuration > 0,
        _ => false
    };

    public override string Summary
    {
        get
        {
            if (!HasValue)
                return string.Empty;

            var time = _timeTicks.HasValue ? new TimeSpan(_timeTicks.Value).ToString(@"hh\:mm") : string.Empty;
            return Item.Kind switch
            {
                InputKind.Numeric or InputKind.Volume => $"{ValueText} {Unit}".Trim(),
                InputKind.Scale => $"{ScaleValue} / {Item.ScaleMax}",
                InputKind.Boolean => BoolState == 1 ? "Yes" : "No",
                InputKind.Dose => DoseState switch
                {
                    1 => string.IsNullOrEmpty(time) ? "Given" : $"Given · {time}",
                    2 => "Skipped",
                    _ => string.Empty
                },
                InputKind.Event => string.Join(" · ", EventSummaryParts(time)),
                InputKind.Text => Shorten(NoteText),
                _ => string.Empty
            };
        }
    }

    private IEnumerable<string> EventSummaryParts(string time)
    {
        if (SeverityValue > 0) yield return $"severity {SeverityValue}";
        if (ParsedDuration > 0) yield return $"{ParsedDuration}m";
        if (!string.IsNullOrEmpty(time)) yield return time;
    }

    // ── Value setters that persist immediately (chip taps) ────────────────
    private async Task SetScaleAsync(string raw)
    {
        if (int.TryParse(raw, out var v))
        {
            ScaleValue = v;
            await SaveAsync();
        }
    }

    private void SetSeverity(string raw)
    {
        // Part of an Event; the "Log" button commits the whole event.
        if (int.TryParse(raw, out var v))
            SeverityValue = v;
    }

    private async Task SetBoolAsync(string raw)
    {
        BoolState = raw == "yes" ? 1 : 2;
        await SaveAsync();
    }

    private async Task SetDoseAsync(string raw)
    {
        DoseState = raw == "given" ? 1 : 2;
        _timeTicks = DateTime.Now.TimeOfDay.Ticks;
        await SaveAsync();
    }

    // ── Load / persist ───────────────────────────────────────────────────
    private void Hydrate(TrackingEntry? e)
    {
        if (e == null)
            return;

        _rowId = e.Id;
        _timeTicks = e.TimeTicks;

        switch (Item.Kind)
        {
            case InputKind.Numeric:
            case InputKind.Volume:
                if (e.Number.HasValue) ValueText = e.Number.Value.ToString();
                break;
            case InputKind.Scale:
                _scaleValue = (int)(e.Number ?? 0);
                break;
            case InputKind.Boolean:
                _boolState = e.Flag == true ? 1 : e.Flag == false ? 2 : 0;
                break;
            case InputKind.Text:
                _noteText = e.Text ?? string.Empty;
                break;
            case InputKind.Dose:
                _doseState = e.Flag == true ? 1 : e.Flag == false ? 2 : 0;
                break;
            case InputKind.Event:
                _severityValue = e.Severity ?? 0;
                _eventNote = e.Text ?? string.Empty;
                if (e.DurationSeconds is int secs && secs > 0)
                    _durationText = (secs / 60).ToString();
                break;
        }
    }

    protected override async Task PersistAsync()
    {
        var entry = new TrackingEntry
        {
            Id = _rowId,
            PetId = _petId,
            Date = _date,
            ItemId = Item.Id
        };

        switch (Item.Kind)
        {
            case InputKind.Numeric:
            case InputKind.Volume:
                entry.Number = TryNumber(out var n) ? n : null;
                break;
            case InputKind.Scale:
                entry.Number = ScaleValue > 0 ? ScaleValue : null;
                break;
            case InputKind.Boolean:
                entry.Flag = BoolState == 1 ? true : BoolState == 2 ? false : (bool?)null;
                break;
            case InputKind.Text:
                entry.Text = string.IsNullOrWhiteSpace(NoteText) ? null : NoteText.Trim();
                break;
            case InputKind.Dose:
                entry.Flag = DoseState == 1 ? true : DoseState == 2 ? false : (bool?)null;
                entry.TimeTicks = _timeTicks;
                break;
            case InputKind.Event:
                entry.Severity = SeverityValue > 0 ? SeverityValue : null;
                entry.DurationSeconds = ParsedDuration > 0 ? ParsedDuration * 60 : null;
                entry.Text = string.IsNullOrWhiteSpace(EventNote) ? null : EventNote.Trim();
                entry.TimeTicks = _timeTicks ??= DateTime.Now.TimeOfDay.Ticks;
                break;
        }

        _rowId = await _service.UpsertAsync(entry);
    }

    private bool TryNumber(out double value)
    {
        value = 0;
        if (InputParser.TryParsePositive(ValueText, out var dec))
        {
            value = (double)dec;
            return true;
        }
        return false;
    }

    private int ParsedDuration => int.TryParse(DurationText, out var m) && m > 0 ? m : 0;

    private static string Shorten(string s)
    {
        s = (s ?? string.Empty).Trim().ReplaceLineEndings(" ");
        return s.Length <= 28 ? s : s[..27] + "…";
    }
}

/// <summary>A drill-in row: Medications, or a condition such as Diabetes. Groups
/// hold child rows (condition items); the Medications group is special-cased to
/// show the day's dose checklist instead.</summary>
public class TrackerGroup : BaseViewModel
{
    public required string Name { get; init; }
    public required string Icon { get; init; }

    /// <summary>True for the Medications group — its content is the dose checklist,
    /// not <see cref="Rows"/>.</summary>
    public bool IsMedications { get; init; }

    public ObservableCollection<TrackerRow> Rows { get; } = new();

    private string _summary = string.Empty;
    /// <summary>Rollup shown on the collapsed group row, e.g. "2/3" or "1 logged".</summary>
    public string Summary
    {
        get => _summary;
        set { if (SetProperty(ref _summary, value)) OnPropertyChanged(nameof(HasSummary)); }
    }

    public bool HasSummary => !string.IsNullOrEmpty(Summary);
}

/// <summary>One chooser row wrapping either a leaf or a group. Refreshes its
/// summary when the underlying leaf is saved.</summary>
public class TrackerRow : BaseViewModel
{
    public TrackerLeaf? Leaf { get; }
    public TrackerGroup? Group { get; }

    public TrackerRow(TrackerLeaf leaf)
    {
        Leaf = leaf;
        leaf.Saved += _ => Refresh();
    }

    public TrackerRow(TrackerGroup group)
    {
        Group = group;
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TrackerGroup.Summary) or nameof(TrackerGroup.HasSummary))
                Refresh();
        };
    }

    public bool IsGroup => Group != null;
    public string Name => Group?.Name ?? Leaf!.Name;
    public string Icon => Group?.Icon ?? Leaf!.Icon;
    public string Summary => Group?.Summary ?? Leaf!.Summary;
    public bool HasSummary => !string.IsNullOrEmpty(Summary);

    /// <summary>Show a ✓ on a leaf row that has been logged (groups show a chevron instead).</summary>
    public bool ShowLeafCheck => !IsGroup && HasSummary;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(ShowLeafCheck));
    }
}
