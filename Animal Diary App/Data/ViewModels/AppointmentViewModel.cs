namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

// ─────────────────────────────────────────────────────────────────────────────
//  The appointment page.
//
//  The vet visit is a STATE, not a destination: it lives quietly on Care and takes
//  over Today when it is near. There is no fourth Shell tab, because a tab is for
//  something used daily and a visit happens two to four times a year.
//
//  Everything below states facts about the record. Counts, dates, the owner's own
//  numbers, and the words they typed themselves — computed identically for every
//  record, including the ones with nothing interesting in them. There is no
//  average, no direction word, no colour or weight that encodes a verdict, and no
//  before/after juxtaposition of two counts around a treatment change. See the
//  doctrine on Data/Models/RecordFacts.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One row of the treatment ledger, as the summary reads it: the date it
/// happened, the medication's name as of then, and the rendered fact. All three come
/// from the stored row — nothing is re-derived from the medication as it stands now,
/// which is what makes it still correct after a rename or a delete.</summary>
public sealed class LedgerLine
{
    public required string Day { get; init; }
    public required string Name { get; init; }

    /// <summary>"30 mg → 45 mg", or the kind's own wording where there is no value to
    /// state (started, archived, stopped).</summary>
    public required string What { get; init; }
}

/// <summary>One record's block in "what you wrote down": its name, its counts, its four
/// day-part bands, and its recorded extremes. Every record gets the same three lines in
/// the same order.</summary>
public sealed class SummaryLine
{
    public required string Name { get; init; }
    public required string Count { get; init; }
    public required string DayParts { get; init; }
    public string Values { get; init; } = string.Empty;
    public bool HasValues => Values.Length > 0;
    public string Doses { get; init; } = string.Empty;
    public bool HasDoses => Doses.Length > 0;
}

/// <summary>One visit in the past list.</summary>
public sealed class VisitLine
{
    public required VetVisit Visit { get; init; }
    public required string When { get; init; }

    /// <summary>Practice and vet, whichever the owner gave. Empty when neither.</summary>
    public string Where { get; init; } = string.Empty;
    public bool HasWhere => Where.Length > 0;

    /// <summary>What the owner wrote down afterwards, verbatim.</summary>
    public string Note { get; init; } = string.Empty;
    public bool HasNote => Note.Length > 0;
}

/// <summary>One open question, with the tick that closes it.</summary>
public sealed class QuestionLine : BaseViewModel
{
    public required int Id { get; init; }
    public required string Text { get; init; }
}

public class AppointmentViewModel : BaseViewModel
{
    private readonly ActivePetService _activePet;
    private readonly VetVisitService _visits;
    private readonly VetQuestionService _questions;
    private readonly AppointmentSummaryService _summaries;
    private readonly VetVisitSheetViewModel _visitSheet;
    private readonly VetQuestionSheetViewModel _questionSheet;

    private int _loadGeneration;
    private VetVisit? _next;
    private VetVisit? _noteCandidate;

    public AppointmentViewModel(
        ActivePetService activePet,
        VetVisitService visits,
        VetQuestionService questions,
        AppointmentSummaryService summaries,
        VetVisitSheetViewModel visitSheet,
        VetQuestionSheetViewModel questionSheet)
    {
        _activePet = activePet;
        _visits = visits;
        _questions = questions;
        _summaries = summaries;
        _visitSheet = visitSheet;
        _questionSheet = questionSheet;

        AddVisitCommand = new Command(async () => await OpenVisitAsync(null));
        OpenVisitCommand = new Command<VisitLine>(async line => await OpenVisitAsync(line?.Visit));
        EditNextCommand = new Command(async () => await OpenVisitAsync(_next));
        WriteNoteCommand = new Command(async () => await OpenVisitAsync(_noteCandidate));
        AddQuestionCommand = new Command(async () => await OpenQuestionSheetAsync());
        AnswerQuestionCommand = new Command<QuestionLine>(async line => await AnswerAsync(line));
        FullSummaryCommand = new Command(() => FullSummaryRequested?.Invoke(SummaryDays));
    }

    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>The page routes "Full summary" to the existing export sheet, pre-filled
    /// with the since-last-visit stretch. There is deliberately no second PDF path —
    /// <c>IVetReportService.GenerateAsync</c> already takes dates.</summary>
    public event Action<int>? FullSummaryRequested;

    public ICommand AddVisitCommand { get; }
    public ICommand OpenVisitCommand { get; }
    public ICommand EditNextCommand { get; }
    public ICommand WriteNoteCommand { get; }
    public ICommand AddQuestionCommand { get; }
    public ICommand AnswerQuestionCommand { get; }
    public ICommand FullSummaryCommand { get; }

    public string Title => Loc.GetString("Vet_VisitsTitle");
    public string Subtitle => Loc.Format("Vet_VisitsSub", _activePet.ActivePet?.Name ?? string.Empty);

    // ── State B: an upcoming visit ───────────────────────────────────────────

    private bool _hasNext;
    /// <summary>An upcoming visit exists — the summary below is about it.</summary>
    public bool HasNext { get => _hasNext; private set => SetProperty(ref _hasNext, value); }

    private string _nextHeadline = string.Empty;
    /// <summary>"CHARLY · Dr. Weiss · Thursday, 15:30" — the pet, whoever the owner
    /// named, and when. No countdown and no urgency: a visit does not get more important
    /// as it approaches, and colouring it as it does would be the app adding alarm the
    /// owner did not ask for.</summary>
    public string NextHeadline { get => _nextHeadline; private set => SetProperty(ref _nextHeadline, value); }

    private string _sinceLine = string.Empty;
    /// <summary>"Since your last visit — 14 March, 159 days", or the "since you started
    /// writing things down" wording when there was no previous visit. The app never
    /// invents one.</summary>
    public string SinceLine { get => _sinceLine; private set => SetProperty(ref _sinceLine, value); }

    /// <summary>How long the summary window is, handed to the export sheet so the PDF
    /// covers exactly the stretch the page just described.</summary>
    public int SummaryDays { get; private set; } = 90;

    public ObservableCollection<LedgerLine> Changes { get; } = new();
    public bool HasChanges => Changes.Count > 0;

    public ObservableCollection<string> NewRecords { get; } = new();
    public bool HasNewRecords => NewRecords.Count > 0;

    public ObservableCollection<SummaryLine> Records { get; } = new();
    public bool HasRecords => Records.Count > 0;

    // ── Questions ────────────────────────────────────────────────────────────

    public ObservableCollection<QuestionLine> Questions { get; } = new();
    public bool HasQuestions => Questions.Count > 0;

    public string QuestionsHeading => Loc.Format("Vet_QuestionsHeading", Questions.Count);

    // ── State A / C: the past ────────────────────────────────────────────────

    public ObservableCollection<VisitLine> PastVisits { get; } = new();
    public bool HasPastVisits => PastVisits.Count > 0;

    private bool _showNotePrompt;
    /// <summary>The most recent visit has happened and nothing has been written down
    /// about it. Offered once, plainly — never repeated, never nagged.</summary>
    public bool ShowNotePrompt { get => _showNotePrompt; private set => SetProperty(ref _showNotePrompt, value); }

    private string _notePromptWhen = string.Empty;
    public string NotePromptWhen { get => _notePromptWhen; private set => SetProperty(ref _notePromptWhen, value); }

    private bool _isEmpty;
    /// <summary>No visits at all. A first-run empty state: it says what the screen is
    /// for and stops (AI/app-voice.md §11).</summary>
    public bool IsEmpty { get => _isEmpty; private set => SetProperty(ref _isEmpty, value); }

    // ── Load ─────────────────────────────────────────────────────────────────

    /// <summary>Read everything this page shows. Safe to call on every appearance and
    /// after any write; a stale load never paints over a newer one.</summary>
    public async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        var pet = _activePet.ActivePet;

        var upcoming = pet is null || pet.Id == 0
            ? new List<VetVisit>()
            : await _visits.GetUpcomingAsync(pet.Id);
        var past = pet is null || pet.Id == 0
            ? new List<VetVisit>()
            : await _visits.GetPastAsync(pet.Id);

        var next = upcoming.FirstOrDefault();

        // The summary is only assembled when there is something to assemble it FOR.
        // It is the most expensive read on the page and it answers a question nobody
        // asked when the next visit does not exist yet.
        var summary = next is null
            ? null
            : await _summaries.BuildAsync(pet);

        // Questions are the one part State A also shows: they accumulate between visits,
        // and a list you can only see once an appointment is booked is a list you stop
        // adding to.
        var questions = pet is null || pet.Id == 0
            ? new List<VetQuestion>()
            : await _questions.GetOpenAsync(pet.Id);

        // A newer load (a pet switch, a second appearance) owns the page now.
        if (generation != _loadGeneration)
            return;

        // ── atomic fill: no awaits from here on ──
        _next = next;
        _noteCandidate = past.FirstOrDefault(v => v.NeedsNote);

        HasNext = next is not null;
        NextHeadline = next is null ? string.Empty : Headline(pet, next);

        BuildSummary(summary);
        BuildQuestions(questions);
        BuildPast(past);

        ShowNotePrompt = _noteCandidate is not null;
        NotePromptWhen = _noteCandidate is null ? string.Empty : DayAndTime(_noteCandidate);
        IsEmpty = next is null && past.Count == 0;

        RaiseAll();
    }

    private void BuildSummary(AppointmentSummary? summary)
    {
        Changes.Clear();
        NewRecords.Clear();
        Records.Clear();

        if (summary is null)
        {
            SinceLine = string.Empty;
            SummaryDays = 90;
            return;
        }

        SummaryDays = summary.Days;
        SinceLine = summary.HasAnchor
            ? Loc.Format("Vet_SinceVisit", Day(summary.From), summary.Days)
            : Loc.Format("Vet_SinceStart", Day(summary.From), summary.Days);

        foreach (var change in summary.Changes)
            Changes.Add(new LedgerLine
            {
                // The ledger is stamped in UTC; the owner reads it in their own time.
                Day = Day(change.ChangedAtUtc.ToLocalTime().Date),
                Name = change.Name(),
                What = change.What(),
            });

        foreach (var record in summary.NewRecords)
            NewRecords.Add(Loc.Format("Vet_NewRecord", record.Name, Day(record.FirstOn)));

        foreach (var record in summary.Records)
            Records.Add(new SummaryLine
            {
                Name = record.Name,
                Count = RecordFactsText.Count(record.Facts),
                DayParts = RecordFactsText.DayParts(record.Facts),
                Values = RecordFactsText.Values(record.Facts),
                Doses = RecordFactsText.Doses(record.Facts),
            });
    }

    private void BuildQuestions(IReadOnlyList<VetQuestion> questions)
    {
        Questions.Clear();
        foreach (var q in questions)
            Questions.Add(new QuestionLine { Id = q.Id, Text = q.Text });
    }

    private void BuildPast(IReadOnlyList<VetVisit> past)
    {
        PastVisits.Clear();
        foreach (var visit in past)
            PastVisits.Add(new VisitLine
            {
                Visit = visit,
                When = DayAndTime(visit),
                Where = Where(visit),
                Note = visit.VisitNote,
            });
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasNewRecords));
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(HasQuestions));
        OnPropertyChanged(nameof(QuestionsHeading));
        OnPropertyChanged(nameof(HasPastVisits));
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private Task OpenVisitAsync(VetVisit? visit)
    {
        var pet = _activePet.ActivePet;
        return _visitSheet.OpenAsync(pet?.Id ?? 0, pet?.Name ?? string.Empty, visit);
    }

    private Task OpenQuestionSheetAsync()
    {
        var pet = _activePet.ActivePet;
        return _questionSheet.OpenAsync(pet?.Id ?? 0, pet?.Name ?? string.Empty, DateTime.Today);
    }

    /// <summary>Tick a question off. It leaves the list; the row itself is kept, so the
    /// answer can be taken back from wherever answered questions are read later.</summary>
    private async Task AnswerAsync(QuestionLine? line)
    {
        if (line is null)
            return;

        await _questions.SetAnsweredAsync(line.Id, true);
        await LoadAsync();
    }

    // ── Wording ──────────────────────────────────────────────────────────────

    /// <summary>"CHARLY · Dr. Weiss · Thursday, 15:30". Whatever the owner did not give
    /// is simply absent — never a placeholder, never "unknown".</summary>
    private static string Headline(Pet? pet, VetVisit visit)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(pet?.Name))
            parts.Add(pet!.Name);

        var who = Where(visit);
        if (who.Length > 0)
            parts.Add(who);

        parts.Add(DayAndTime(visit));
        return string.Join(" · ", parts);
    }

    /// <summary>The vet's name, the practice, or both — whichever the owner typed.</summary>
    private static string Where(VetVisit visit)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(visit.VetName))
            parts.Add(visit.VetName.Trim());
        if (!string.IsNullOrWhiteSpace(visit.Practice))
            parts.Add(visit.Practice.Trim());
        return string.Join(" · ", parts);
    }

    /// <summary>"Thursday, 15:30", or just the day when the owner only knew the day.</summary>
    private static string DayAndTime(VetVisit visit)
    {
        var day = visit.Date.ToString("dddd, d MMM", CultureInfo.CurrentCulture);
        return visit.Time is TimeSpan t
            ? $"{day}, {t:hh\\:mm}"
            : day;
    }

    private static string Day(DateTime date) => date.ToString("d MMM", CultureInfo.CurrentCulture);
}

/// <summary>Wording for one ledger row. It lives here rather than on the model because
/// the model is a stored row and must not resolve a localized string at read time — a
/// singleton that cached one would freeze in the language it was built in.</summary>
internal static class LedgerLineText
{
    /// <summary>The medication's name as of the change. Verbatim user text.</summary>
    public static string Name(this MedicationChange change) => change.MedicationName;

    /// <summary>
    /// The fact. A dose or schedule change carries its own rendered summary; the kinds
    /// with no value to state get a word instead.
    ///
    /// <para>Chronological facts only — nothing here says whether a change was an
    /// increase worth noting, and nothing may put two counts either side of one.</para>
    /// </summary>
    public static string What(this MedicationChange change)
    {
        if (!string.IsNullOrWhiteSpace(change.Summary))
            return change.Kind == MedicationChangeKind.Started
                ? LocalizationManager.Instance.Format("Vet_LedgerStarted", change.Summary)
                : change.Summary;

        return LocalizationManager.Instance.GetString(change.Kind switch
        {
            MedicationChangeKind.Archived => "Vet_LedgerArchived",
            MedicationChangeKind.Restored => "Vet_LedgerRestored",
            MedicationChangeKind.Stopped => "Vet_LedgerStopped",
            _ => "Vet_LedgerChanged",
        });
    }
}
