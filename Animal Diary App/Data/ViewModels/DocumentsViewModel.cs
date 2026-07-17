namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Reports;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Documents page: the ACTIVE pet's generated vet reports, newest
/// first, with view / share / delete-with-undo. Deletion is deferred, matching
/// the app's undo-toast pattern: the row leaves the list immediately, but the
/// file+row are only deleted when the toast expires (or the page is left);
/// Undo simply puts the row back. Only one delete is in flight at a time —
/// staging a new one commits the previous first.
/// </summary>
public class DocumentsViewModel : BaseViewModel
{
    private readonly ReportLibraryService _library;
    private readonly ActivePetService _activePetService;

    private (ReportListItem Item, int Index)? _pendingDelete;

    public DocumentsViewModel(ReportLibraryService library, ActivePetService activePetService)
    {
        _library = library;
        _activePetService = activePetService;

        OpenCommand = new Command<ReportListItem>(item => OpenRequested?.Invoke(item.Report));
        ShareCommand = new Command<ReportListItem>(async item => await ShareAsync(item));
        DeleteCommand = new Command<ReportListItem>(async item => await StageDeleteAsync(item));
    }

    /// <summary>Row tapped — the hosting page pushes the preview (pages navigate, VMs don't).</summary>
    public event Action<VetReportFile>? OpenRequested;

    /// <summary>A delete was staged — the hosting page shows the undo toast with
    /// <see cref="UndoDeleteAsync"/> / <see cref="CommitPendingDeleteAsync"/> as its callbacks.</summary>
    public event Action<string>? DeleteStaged;

    public ObservableCollection<ReportListItem> Reports { get; } = new();

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    // Localized per read — a singleton VM must survive a live language switch.
    public string Title => LocalizationManager.Instance.GetString("Docs_Title");
    public string Subtitle => LocalizationManager.Instance.Format("Docs_Subtitle", _activePetService.ActivePet.Name);

    public ICommand OpenCommand { get; }
    public ICommand ShareCommand { get; }
    public ICommand DeleteCommand { get; }

    public async Task LoadAsync()
    {
        // A pending delete from a previous visit must not resurrect on reload.
        await CommitPendingDeleteAsync();

        Reports.Clear();
        var petId = _activePetService.ActivePet.Id;
        if (petId != 0)
            foreach (var row in await _library.GetForPetAsync(petId))
                Reports.Add(new ReportListItem(row));

        IsEmpty = Reports.Count == 0;
        OnPropertyChanged(nameof(Subtitle));
    }

    private async Task StageDeleteAsync(ReportListItem item)
    {
        await CommitPendingDeleteAsync(); // only one staged delete at a time

        var index = Reports.IndexOf(item);
        if (index < 0)
            return;

        Reports.RemoveAt(index);
        _pendingDelete = (item, index);
        IsEmpty = Reports.Count == 0;

        DeleteStaged?.Invoke(LocalizationManager.Instance.GetString("Docs_Deleted"));
    }

    /// <summary>The toast's Undo: put the row back where it was; nothing was
    /// deleted from disk yet.</summary>
    public Task UndoDeleteAsync()
    {
        if (_pendingDelete is { } pending)
        {
            Reports.Insert(Math.Min(pending.Index, Reports.Count), pending.Item);
            _pendingDelete = null;
            IsEmpty = false;
        }
        return Task.CompletedTask;
    }

    /// <summary>Actually delete the staged report (toast expired, page left, or a
    /// new delete/reload superseded it). Idempotent — callers may overlap.</summary>
    public async Task CommitPendingDeleteAsync()
    {
        if (_pendingDelete is not { } pending)
            return;
        _pendingDelete = null;
        await _library.DeleteAsync(pending.Item.Report);
    }

    private static async Task ShareAsync(ReportListItem item)
    {
        try
        {
            await ReportActions.ShareAsync(item.Report);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Documents] share failed: {ex}");
        }
    }
}

/// <summary>One Documents row: the library record plus its display strings.
/// Rows are rebuilt on every page load, so the formatted strings follow the
/// current culture without change notification.</summary>
public class ReportListItem
{
    public ReportListItem(VetReportFile report) => Report = report;

    public VetReportFile Report { get; }

    /// <summary>First preview page doubles as the row thumbnail.</summary>
    public string ThumbnailPath => ReportLibraryService.PreviewPathFor(Report, 1);

    public string PeriodDisplay =>
        $"{Report.FromDate:dd MMM yyyy} – {Report.ToDate:dd MMM yyyy}";

    public string CreatedDisplay => Report.CreatedAt.ToString("g");

    /// <summary>"2 pages · 1.4 MB" (page word localized, size unit universal).</summary>
    public string MetaDisplay =>
        $"{LocalizationManager.Instance.Format("Docs_Pages", Report.PageCount)} · {Report.SizeBytes / 1024.0 / 1024.0:0.0} MB";
}
