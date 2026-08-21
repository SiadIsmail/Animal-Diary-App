namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.Services.Reports;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Pets page's "Export" sheet — pick a period (30/90/180 days),
/// generate the vet PDF, then View / Share / Done, all inside the shared
/// <c>FelovaBottomSheet</c> (never an alert). Not a Journal
/// sheet, so there is no <c>Saved</c> event; instead <see cref="ViewRequested"/>
/// lets the hosting page push the preview (navigation belongs to pages).
///
/// The sheet has two faces switched by <see cref="IsDone"/>: the options face
/// (chips + create button, with inline no-data/error text) and the done face
/// (saved line + actions). <see cref="IsGenerating"/> guards re-entry and turns
/// the create button into a progress state.
///
/// <para><b>Two documents, and the line between them.</b> The DESIGNED report is paid;
/// the PLAIN log is free forever, on every tier, and is what keeps "getting your data out
/// is never blocked". A free owner is shown both at equal weight
/// (<see cref="ShowReportChoice"/>) — the plain export is never the small print under a
/// sale, because the point of it is that nobody is ever trapped.</para>
/// </summary>
public class ExportSheetViewModel : BaseViewModel
{
    private readonly IVetReportService _reports;
    private readonly ActivePetService _activePetService;
    private readonly WaterEntryService _water;
    private readonly AppetiteEntryService _appetite;
    private readonly PetEntryService _petEntries;
    private readonly CustomTrackerService _custom;
    private readonly IAnalyticsService _analytics;
    private readonly Animal_Diary_App.Data.Services.Billing.IEntitlementService _entitlements;
    private readonly SubscribeSheetViewModel _subscribe;

    private Pet _pet = new();
    private VetReportFile? _result;

    public ExportSheetViewModel(IVetReportService reports, ActivePetService activePetService,
        WaterEntryService water, AppetiteEntryService appetite, PetEntryService petEntries,
        CustomTrackerService custom, IAnalyticsService analytics,
        Animal_Diary_App.Data.Services.Billing.IEntitlementService entitlements,
        SubscribeSheetViewModel subscribe)
    {
        _reports = reports;
        _activePetService = activePetService;
        _water = water;
        _appetite = appetite;
        _petEntries = petEntries;
        _custom = custom;
        _analytics = analytics;
        _entitlements = entitlements;
        _subscribe = subscribe;

        OpenCommand = new Command(async () => await OpenAsync());
        DismissCommand = new Command(() => IsPresented = false);
        SelectPeriodCommand = new Command<string>(SelectPeriod);
        SelectSinceVisitCommand = new Command(() => { if (HasSinceVisit) SelectedDays = SinceVisitDays; });
        ToggleIncludePhotoCommand = new Command(() => IncludePhoto = !IncludePhoto);
        ToggleIncludeWaterMeasuredCommand = new Command(() => IncludeWaterMeasured = !IncludeWaterMeasured);
        ToggleIncludeWaterObservationsCommand = new Command(() => IncludeWaterObservations = !IncludeWaterObservations);
        ToggleIncludeAppetiteMeasuredCommand = new Command(() => IncludeAppetiteMeasured = !IncludeAppetiteMeasured);
        ToggleIncludeAppetiteObservationsCommand = new Command(() => IncludeAppetiteObservations = !IncludeAppetiteObservations);
        ToggleIncludeMoodCommand = new Command(() => IncludeMood = !IncludeMood);
        ToggleIncludeCustomCommand = new Command(() => IncludeCustom = !IncludeCustom);
        GenerateCommand = new Command(async () => await GenerateAsync());
        GeneratePlainCommand = new Command(async () => await GeneratePlainAsync());
        SubscribeCommand = new Command(() =>
        {
            IsPresented = false;
            _subscribe.Open(AnalyticsEvents.SubscribeSourceReport);
        });
        ViewCommand = new Command(() =>
        {
            if (_result != null)
                ViewRequested?.Invoke(_result);
        });
        ShareCommand = new Command(async () => await ShareAsync());
    }

    /// <summary>Raised when the user taps "View" — the hosting page pushes the
    /// preview page for this report (the VM never navigates).</summary>
    public event Action<VetReportFile>? ViewRequested;

    /// <summary>Save the plain chronological log. Free on every tier.</summary>
    public ICommand GeneratePlainCommand { get; }

    /// <summary>Open the subscribe sheet from the report door.</summary>
    public ICommand SubscribeCommand { get; }

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    // Localized per read — a singleton VM must survive a live language switch.
    public string Title => LocalizationManager.Instance.GetString("Export_SheetTitle");
    public string Subtitle => LocalizationManager.Instance.Format("Export_SheetSubtitle", _pet.Name);

    /// <summary>How far back the report looks. The chips are the only UI for this
    /// today; a custom range would extend here (GenerateAsync already takes dates).</summary>
    private int _selectedDays = 90;
    public int SelectedDays
    {
        get => _selectedDays;
        set
        {
            if (SetProperty(ref _selectedDays, value))
                OnPropertyChanged(nameof(IsSinceVisitSelected));
        }
    }

    /// <summary>
    /// The since-your-last-visit stretch, when the appointment page opened this sheet;
    /// 0 otherwise.
    ///
    /// <para>It gets a chip of its OWN rather than silently selecting nothing. A
    /// hundred-and-fifty-nine-day window matches none of the three presets, and a sheet
    /// that opens with no chip lit reads as broken — the same rule the care-plan adjust
    /// sheet follows: every rung the sheet can save is one it displayed
    /// (AI/adding-a-tracker.md).</para>
    /// </summary>
    private int _sinceVisitDays;
    public int SinceVisitDays
    {
        get => _sinceVisitDays;
        private set
        {
            if (SetProperty(ref _sinceVisitDays, value))
            {
                OnPropertyChanged(nameof(HasSinceVisit));
                OnPropertyChanged(nameof(SinceVisitLabel));
                OnPropertyChanged(nameof(IsSinceVisitSelected));
            }
        }
    }

    public bool HasSinceVisit => SinceVisitDays > 0;

    /// <summary>Highlights the extra chip. A DataTrigger cannot compare against a value
    /// that changes per open, so the comparison lives here.</summary>
    public bool IsSinceVisitSelected => HasSinceVisit && SelectedDays == SinceVisitDays;

    public string SinceVisitLabel =>
        LocalizationManager.Instance.Format("Export_SinceVisit", SinceVisitDays);

    /// <summary>Whether the active pet actually has a photo file on this device — the
    /// "Include photo" toggle is only shown when true.</summary>
    private bool _hasPhoto;
    public bool HasPhoto { get => _hasPhoto; private set => SetProperty(ref _hasPhoto, value); }

    /// <summary>Opt-in: include the pet's profile photo in the report header. Default
    /// off (the report is data-minimized); reset every time the sheet opens.</summary>
    private bool _includePhoto;
    public bool IncludePhoto { get => _includePhoto; set => SetProperty(ref _includePhoto, value); }

    /// <summary>Whether the pet has ever logged any water — gates the two water
    /// toggles (only shown when there's water to include, mirroring HasPhoto).</summary>
    private bool _hasWater;
    public bool HasWater { get => _hasWater; private set => SetProperty(ref _hasWater, value); }

    /// <summary>Include the objective measured (mL) water graph. Default ON. Kept
    /// separate from observations — the report never merges or interprets the two.</summary>
    private bool _includeWaterMeasured = true;
    public bool IncludeWaterMeasured { get => _includeWaterMeasured; set => SetProperty(ref _includeWaterMeasured, value); }

    /// <summary>Include the subjective owner-observations water graph. Default ON.</summary>
    private bool _includeWaterObservations = true;
    public bool IncludeWaterObservations { get => _includeWaterObservations; set => SetProperty(ref _includeWaterObservations, value); }

    /// <summary>Whether the pet has ever logged any appetite — gates the appetite toggles.</summary>
    private bool _hasAppetite;
    public bool HasAppetite { get => _hasAppetite; private set => SetProperty(ref _hasAppetite, value); }

    /// <summary>Include the objective measured (grams) appetite graph. Default ON.</summary>
    private bool _includeAppetiteMeasured = true;
    public bool IncludeAppetiteMeasured { get => _includeAppetiteMeasured; set => SetProperty(ref _includeAppetiteMeasured, value); }

    /// <summary>Include the subjective observations appetite graph + diet list. Default ON.</summary>
    private bool _includeAppetiteObservations = true;
    public bool IncludeAppetiteObservations { get => _includeAppetiteObservations; set => SetProperty(ref _includeAppetiteObservations, value); }

    /// <summary>Whether the pet has ever logged a mood — gates the mood toggle.</summary>
    private bool _hasMood;
    public bool HasMood { get => _hasMood; private set => SetProperty(ref _hasMood, value); }

    /// <summary>Include the daily mood graph. Default ON, like every other metric.
    /// Mood is observations only — there is no measured counterpart, so it is a single
    /// toggle rather than the measured/observed pair water and appetite carry.</summary>
    private bool _includeMood = true;
    public bool IncludeMood { get => _includeMood; set => SetProperty(ref _includeMood, value); }

    /// <summary>Whether the pet has any owner-defined tracker that BOTH opted into the
    /// report and has been logged — the gate on showing this toggle at all.</summary>
    private bool _hasCustom;
    public bool HasCustom { get => _hasCustom; private set => SetProperty(ref _hasCustom, value); }

    /// <summary>ONE toggle for the whole owner-defined section, not one per tracker.
    ///
    /// <para>Whether a walk belongs in front of a vet is a property of the tracker, and
    /// it is answered once on the tracker itself. Re-asking here would put the same
    /// question on a screen people reach while worried, and would grow this sheet by a
    /// row for every tracker they ever made. This is only the usual per-export escape
    /// hatch, like every other toggle here.</para></summary>
    private bool _includeCustom = true;
    public bool IncludeCustom { get => _includeCustom; set => SetProperty(ref _includeCustom, value); }

    private bool _isGenerating;
    public bool IsGenerating { get => _isGenerating; set => SetProperty(ref _isGenerating, value); }

    private bool _isDone;
    public bool IsDone { get => _isDone; set => SetProperty(ref _isDone, value); }

    /// <summary>Inline "no data" / error line on the options face; empty = hidden.</summary>
    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

    public string DoneMessage => LocalizationManager.Instance.Format("Export_DoneMessage", SelectedDays);
    public string ResultFileName => _result?.FileName ?? string.Empty;

    public ICommand OpenCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand SelectPeriodCommand { get; }
    public ICommand SelectSinceVisitCommand { get; }
    public ICommand ToggleIncludePhotoCommand { get; }
    public ICommand ToggleIncludeWaterMeasuredCommand { get; }
    public ICommand ToggleIncludeWaterObservationsCommand { get; }
    public ICommand ToggleIncludeAppetiteMeasuredCommand { get; }
    public ICommand ToggleIncludeAppetiteObservationsCommand { get; }
    public ICommand ToggleIncludeMoodCommand { get; }
    public ICommand ToggleIncludeCustomCommand { get; }
    public ICommand GenerateCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand ShareCommand { get; }

    /// <summary>Open pre-filled with a stretch the caller worked out — the appointment
    /// page's "Full summary", which hands over exactly the window it just described.
    /// Nothing else about the sheet changes: same toggles, same one PDF path.</summary>
    public Task OpenForRangeAsync(int days) => OpenAsync(days > 0 ? days : null);

    private async Task OpenAsync(int? days = null)
    {
        _pet = _activePetService.ActivePet;

        // Fresh interaction every time — a previous export's result must not leak.
        _result = null;
        IsDone = false;
        IsGenerating = false;
        StatusMessage = string.Empty;

        // A caller-supplied stretch brings its own chip; otherwise the extra chip is
        // cleared so a previous appointment's window can never linger into a plain open.
        SinceVisitDays = days ?? 0;
        SelectedDays = days ?? 90;

        // Photo opt-in defaults off every open; the toggle only shows when the pet has
        // a photo file present on this device.
        IncludePhoto = false;
        HasPhoto = _pet.PhotoFullPath is { } p && File.Exists(p);

        // Water + appetite: both types included by default; each metric's toggles only
        // show when the pet has logged it (mirrors the photo toggle's "only when relevant").
        IncludeWaterMeasured = true;
        IncludeWaterObservations = true;
        HasWater = _pet.Id != 0 && await _water.HasAnyAsync(_pet.Id);

        IncludeAppetiteMeasured = true;
        IncludeAppetiteObservations = true;
        HasAppetite = _pet.Id != 0 && await _appetite.HasAnyAsync(_pet.Id);

        // Mood: same "on by default, hidden when never logged" rule. One reading is
        // enough for the toggle to matter — an owner who logged a mood once and doesn't
        // want it in front of their vet can simply untick it.
        IncludeMood = true;
        HasMood = _pet.Id != 0 && await _petEntries.GetLatestMoodEntryAsync(_pet.Id) is not null;

        // Same "on by default, hidden when there is nothing to include" rule. Gated on a
        // tracker that opted in AND has entries — a pet with only a Walk tracker (switched
        // off on the tracker) sees no toggle, because there would be nothing behind it.
        IncludeCustom = true;
        HasCustom = _pet.Id != 0 && await _custom.HasReportableEntriesAsync(_pet.Id);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        // Re-read every open: the active pet changes, and a purchase or a redeemed code
        // can land between two openings of this sheet.
        OnPropertyChanged(nameof(ChoiceIntro));
        OnPropertyChanged(nameof(CanUseDesignedReport));
        OnPropertyChanged(nameof(ShowReportChoice));
        OnPropertyChanged(nameof(ShowSectionOptions));
        IsPresented = true;

        // Feature-discovery signal: paired with report_exported this yields an
        // open→export conversion rate (did they abandon at the options screen?).
        _analytics.Track(AnalyticsEvents.ExportSheetOpened);
    }

    /// <summary>Lead line over the two documents. Names the pet, because this is the one
    /// place the sheet says what both options are FOR.</summary>
    public string ChoiceIntro => LocalizationManager.Instance.Format("Export_ChoiceIntro", _pet.Name);

    /// <summary>Whether the designed report is available for the pet being exported.
    /// Pet-scoped, so a caregiver on a subscribed owner's animal gets it without buying
    /// their own.</summary>
    public bool CanUseDesignedReport => _entitlements.CanEditPet(_pet.SyncId);

    /// <summary>Free owner: show both documents, at equal weight, and neither dressed as
    /// the lesser one. Read fresh on every open — a purchase can land between two.</summary>
    public bool ShowReportChoice => !CanUseDesignedReport;

    /// <summary>The per-section toggles configure the DESIGNED report only. Hiding them
    /// when it is not available is not a lock; it is not asking someone to tune knobs on
    /// a document they are not making. The period chips stay, because they apply to
    /// both.</summary>
    public bool ShowSectionOptions => CanUseDesignedReport;

    private void SelectPeriod(string days)
    {
        if (int.TryParse(days, out var parsed) && parsed > 0)
            SelectedDays = parsed;
    }

    /// <summary>The FREE export. Never gated, never configurable, and deliberately not
    /// routed through <see cref="GenerateAsync"/>'s include-flags: it is everything that
    /// was written down, in order, and there is nothing to decide.</summary>
    private async Task GeneratePlainAsync()
    {
        if (IsGenerating)
            return;
        IsGenerating = true;
        StatusMessage = string.Empty;

        try
        {
            _result = _pet.Id == 0
                ? null
                : await _reports.GeneratePlainAsync(
                    _pet.Id, DateTime.Today.AddDays(-SelectedDays), DateTime.Today);

            if (_result == null)
            {
                StatusMessage = LocalizationManager.Instance.GetString("Export_NoData");
                return;
            }

            OnPropertyChanged(nameof(DoneMessage));
            OnPropertyChanged(nameof(ResultFileName));
            IsDone = true;

            // Same event as the designed report, with the kind alongside the window. It is
            // one funnel: the question is whether people get their data out at all, and
            // splitting it into two events would make the free half look like a
            // second-class feature in the numbers as well as on the sheet.
            _analytics.Track(AnalyticsEvents.ReportExported, new Dictionary<string, object?>
            {
                [AnalyticsEvents.PropRangeDays] = SelectedDays,
                [AnalyticsEvents.PropReportKind] = AnalyticsEvents.ReportKindPlain,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] plain export failed: {ex}");
            StatusMessage = LocalizationManager.Instance.GetString("Export_Failed");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task GenerateAsync()
    {
        if (IsGenerating)
            return;
        // The designed report is the paid artifact. Reached only from a button that is
        // hidden without access, so this is a backstop rather than the gate.
        if (!CanUseDesignedReport)
        {
            SubscribeCommand.Execute(null);
            return;
        }
        IsGenerating = true;
        StatusMessage = string.Empty;

        try
        {
            // One path. The compile-time sample-data switch that used to wrap this is gone:
            // a demo pet's report is generated from real rows through this very call, so
            // filming an export and shipping one are now literally the same code.
            _result = _pet.Id == 0
                ? null // no active pet behaves like "no data"
                : await _reports.GenerateAsync(
                    _pet.Id, DateTime.Today.AddDays(-SelectedDays), DateTime.Today,
                    IncludePhoto, IncludeWaterMeasured, IncludeWaterObservations,
                    IncludeAppetiteMeasured, IncludeAppetiteObservations, IncludeMood,
                    IncludeCustom);

            if (_result == null)
            {
                StatusMessage = LocalizationManager.Instance.GetString("Export_NoData");
                return;
            }

            OnPropertyChanged(nameof(DoneMessage));
            OnPropertyChanged(nameof(ResultFileName));
            IsDone = true;

            // "Which features provide value?" — a vet report was actually produced. We
            // send only the chosen look-back window; nothing about the pet or its data.
            _analytics.Track(AnalyticsEvents.ReportExported, new Dictionary<string, object?>
            {
                [AnalyticsEvents.PropRangeDays] = SelectedDays,
                [AnalyticsEvents.PropReportKind] = AnalyticsEvents.ReportKindDesigned,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] export failed: {ex}");
            StatusMessage = LocalizationManager.Instance.GetString("Export_Failed");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private async Task ShareAsync()
    {
        if (_result == null)
            return;
        try
        {
            await ReportActions.ShareAsync(_result);
        }
        catch (Exception ex)
        {
            // The OS share sheet failing (no targets, cancelled provider) must not
            // crash the app; the export itself already succeeded.
            System.Diagnostics.Debug.WriteLine($"[VetReport] share failed: {ex}");
        }
    }
}
