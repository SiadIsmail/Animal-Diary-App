namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Import;

/// <summary>
/// The importer screen: pick a file, look at what it will do, confirm.
///
/// <para><b>English-only by intent</b>, like <see cref="DevSheetViewModel"/> — this is an
/// internal tool reached by a code, not a shipped feature, and localizing a surface that
/// exists to be replaced would be work spent on the wrong thing. Every user-facing string
/// in the app proper is still localized; this one is deliberately outside that rule and
/// should be brought inside it the day the importer stops being hidden.</para>
///
/// <para>The three states are one property (<see cref="Stage"/>) rather than three
/// booleans that can disagree.</para>
/// </summary>
public sealed class ImportViewModel : BaseViewModel, IResettableDraft
{
    private readonly ImportService _import;
    private readonly ActivePetService _activePet;

    public ImportViewModel(ImportService import, ActivePetService activePet)
    {
        _import = import;
        _activePet = activePet;

        PickCommand = new Command(async () => await PickAsync());
        ConfirmCommand = new Command(async () => await ConfirmAsync());
        StartOverCommand = new Command(Reset);
    }

    public enum ImportStage
    {
        /// <summary>Nothing chosen yet.</summary>
        Empty,

        /// <summary>The file was read and rejected. <see cref="Problems"/> says why.</summary>
        Rejected,

        /// <summary>Validated. <see cref="Summaries"/> is what will happen if confirmed.</summary>
        Preview,

        /// <summary>Written.</summary>
        Done
    }

    // ── State ───────────────────────────────────────────────────────────────────

    private ImportStage _stage = ImportStage.Empty;
    public ImportStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value))
                return;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsRejected));
            OnPropertyChanged(nameof(IsPreview));
            OnPropertyChanged(nameof(IsDone));
        }
    }

    public bool IsEmpty => Stage == ImportStage.Empty;
    public bool IsRejected => Stage == ImportStage.Rejected;
    public bool IsPreview => Stage == ImportStage.Preview;
    public bool IsDone => Stage == ImportStage.Done;

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
                OnPropertyChanged(nameof(Idle));
        }
    }

    public bool Idle => !_busy;

    private string _fileName = string.Empty;
    public string FileName { get => _fileName; private set => SetProperty(ref _fileName, value); }

    private string _headline = string.Empty;
    /// <summary>The one line at the top of whichever state is showing.</summary>
    public string Headline { get => _headline; private set => SetProperty(ref _headline, value); }

    /// <summary>Per-pet summaries — the preview's body.</summary>
    public ObservableCollection<string> Summaries { get; } = new();

    /// <summary>Errors when rejected; notices when previewing. Both answer "what should I
    /// know before this happens", so they share a list rather than competing for the same
    /// space with two.</summary>
    public ObservableCollection<string> Problems { get; } = new();

    private string _problemsHeader = string.Empty;
    public string ProblemsHeader { get => _problemsHeader; private set => SetProperty(ref _problemsHeader, value); }

    public bool HasProblems => Problems.Count > 0;

    public ICommand PickCommand { get; }
    public ICommand ConfirmCommand { get; }
    public ICommand StartOverCommand { get; }

    // ── Pick + validate ─────────────────────────────────────────────────────────

    private ImportPlan? _plan;
    private string? _rawText;

    private async Task PickAsync()
    {
        if (Busy)
            return;

        Busy = true;
        try
        {
            // No custom file type is declared. Android's picker is inconsistent about
            // application/json for a file the user saved from a chat app, and a wrong
            // filter shows an empty folder with nothing to explain it — the parser rejects
            // anything that is not an import file a moment later anyway, with a message
            // that says so.
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a Felova import file",
            });

            if (picked is null)
                return;

            FileName = picked.FileName;

            using var stream = await picked.OpenReadAsync();
            using var reader = new StreamReader(stream);
            _rawText = await reader.ReadToEndAsync();

            _plan = await _import.PrepareAsync(_rawText);
            Render(_plan);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Import] pick failed: {ex}");
            ShowRejection(new[] { $"The file could not be read: {ex.Message}" });
        }
        finally
        {
            Busy = false;
        }
    }

    private void Render(ImportPlan plan)
    {
        if (!plan.IsValid)
        {
            ShowRejection(plan.Errors.Select(e => e.ToString()).ToList());
            return;
        }

        // Build fully, then swap in one synchronous block (AI/coding-standards.md).
        var summaries = plan.Pets.Select(Describe).ToList();
        var notices = plan.AllNotices.Select(n => n.Message).Distinct().ToList();

        Summaries.Clear();
        foreach (var line in summaries)
            Summaries.Add(line);

        Problems.Clear();
        foreach (var line in notices)
            Problems.Add(line);

        ProblemsHeader = notices.Count == 0 ? string.Empty : "Before you confirm";
        OnPropertyChanged(nameof(HasProblems));

        Headline = plan.IsEmpty
            ? "Nothing new to import — everything in this file is already recorded."
            : $"{plan.TotalEntryCount} " + (plan.TotalEntryCount == 1 ? "entry" : "entries") +
              $" across {plan.Pets.Count} " + (plan.Pets.Count == 1 ? "pet" : "pets") +
              (plan.TotalSkippedCount > 0 ? $" · {plan.TotalSkippedCount} skipped" : string.Empty);

        Stage = ImportStage.Preview;
    }

    /// <summary>One pet's line in the preview. States the destination first, because
    /// "creates a new pet" and "adds to the pet you already have" are the two outcomes
    /// worth catching before confirming.</summary>
    private static string Describe(PlannedPet pet)
    {
        var destination = pet.NewPet is { } created
            ? $"NEW pet \"{created.Name}\" ({created.Species}, born {created.BirthYear})"
            : $"\"{pet.Name}\" (already in Felova)";

        var parts = new List<string> { $"{pet.EntryCount} " + (pet.EntryCount == 1 ? "entry" : "entries") };

        if (pet.NewTrackerCount > 0)
            parts.Add($"{pet.NewTrackerCount} new tracker" + (pet.NewTrackerCount == 1 ? string.Empty : "s"));

        if (pet.SkippedCount > 0)
            parts.Add($"{pet.SkippedCount} skipped");

        if (pet.DateRange is { } range)
            parts.Add($"{range.From:yyyy-MM-dd} to {range.To:yyyy-MM-dd}");

        return $"{destination}\n{string.Join(" · ", parts)}";
    }

    private void ShowRejection(IReadOnlyList<string> problems)
    {
        Summaries.Clear();

        var lines = problems.ToList();
        Problems.Clear();
        foreach (var line in lines)
            Problems.Add(line);

        ProblemsHeader = "Nothing was imported";
        OnPropertyChanged(nameof(HasProblems));

        Headline = lines.Count == 1
            ? "This file cannot be imported."
            : $"This file cannot be imported — {lines.Count} problems.";

        _plan = null;
        Stage = ImportStage.Rejected;
    }

    // ── Confirm ─────────────────────────────────────────────────────────────────

    private async Task ConfirmAsync()
    {
        if (Busy || _plan is null || !_plan.IsValid)
            return;

        Busy = true;
        try
        {
            var result = await _import.CommitAsync(_plan, _rawText);

            // A newly created pet the owner cannot find is indistinguishable from a
            // failed import, so the importer makes it the active one. Appending to an
            // existing pet deliberately does NOT switch — the owner was already looking
            // at whichever pet they meant.
            if (result.FirstNewPetId != 0)
            {
                try { await _activePet.LoadActivePetAsync(result.FirstNewPetId); }
                catch (Exception ex) { Debug.WriteLine($"[Import] could not activate the new pet: {ex.Message}"); }
            }

            var parts = new List<string>();
            if (result.PetsCreated > 0)
                parts.Add($"{result.PetsCreated} pet" + (result.PetsCreated == 1 ? string.Empty : "s") + " created");
            if (result.TrackersCreated > 0)
                parts.Add($"{result.TrackersCreated} tracker" + (result.TrackersCreated == 1 ? string.Empty : "s") + " added");
            parts.Add($"{result.EntriesWritten} " + (result.EntriesWritten == 1 ? "entry" : "entries") + " written");

            Summaries.Clear();
            Problems.Clear();
            ProblemsHeader = string.Empty;
            OnPropertyChanged(nameof(HasProblems));

            Headline = string.Join(" · ", parts);
            Stage = ImportStage.Done;
        }
        catch (Exception ex)
        {
            // The write is one transaction, so a failure here left nothing behind. Say so
            // plainly — "it half worked" is the fear this message exists to answer.
            Debug.WriteLine($"[Import] commit failed: {ex}");
            ShowRejection(new[] { $"The import failed and nothing was written: {ex.Message}" });
        }
        finally
        {
            Busy = false;
        }
    }

    // ── Reset ───────────────────────────────────────────────────────────────────

    private void Reset()
    {
        _plan = null;
        _rawText = null;
        FileName = string.Empty;
        Headline = string.Empty;
        ProblemsHeader = string.Empty;
        Summaries.Clear();
        Problems.Clear();
        OnPropertyChanged(nameof(HasProblems));
        Busy = false;
        Stage = ImportStage.Empty;
    }

    public void ResetDraft() => Reset();
}
