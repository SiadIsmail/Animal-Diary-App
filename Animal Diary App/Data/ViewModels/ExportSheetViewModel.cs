namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Reports;
using Animal_Diary_App.Helpers;

/// <summary>
/// Backs the Pets page's "Export" sheet — pick a period (30/90/180 days),
/// generate the vet PDF, then View / Share / Done, all inside the shared
/// <see cref="Controls.FelovaBottomSheet"/> (never an alert). Not a Journal
/// sheet, so there is no <c>Saved</c> event; instead <see cref="ViewRequested"/>
/// lets the hosting page push the preview (navigation belongs to pages).
///
/// The sheet has two faces switched by <see cref="IsDone"/>: the options face
/// (chips + create button, with inline no-data/error text) and the done face
/// (saved line + actions). <see cref="IsGenerating"/> guards re-entry and turns
/// the create button into a progress state.
/// </summary>
public class ExportSheetViewModel : BaseViewModel
{
    private readonly IVetReportService _reports;
    private readonly ActivePetService _activePetService;
    private readonly IAnalyticsService _analytics;

    /// <summary>Flip to true to generate from <see cref="VetReportSampleData"/>'s
    /// fake data — iterate on the PDF layout without real logged entries. Sample
    /// files are saved (View/Share work) but never listed in Documents.</summary>
    private const bool UseSampleReportData = false;

    private Pet _pet = new();
    private VetReportFile? _result;

    public ExportSheetViewModel(IVetReportService reports, ActivePetService activePetService, IAnalyticsService analytics)
    {
        _reports = reports;
        _activePetService = activePetService;
        _analytics = analytics;

        OpenCommand = new Command(Open);
        DismissCommand = new Command(() => IsPresented = false);
        SelectPeriodCommand = new Command<string>(SelectPeriod);
        ToggleIncludePhotoCommand = new Command(() => IncludePhoto = !IncludePhoto);
        GenerateCommand = new Command(async () => await GenerateAsync());
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

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    // Localized per read — a singleton VM must survive a live language switch.
    public string Title => LocalizationManager.Instance.GetString("Export_SheetTitle");
    public string Subtitle => LocalizationManager.Instance.Format("Export_SheetSubtitle", _pet.Name);

    /// <summary>How far back the report looks. The chips are the only UI for this
    /// today; a custom range would extend here (GenerateAsync already takes dates).</summary>
    private int _selectedDays = 90;
    public int SelectedDays { get => _selectedDays; set => SetProperty(ref _selectedDays, value); }

    /// <summary>Whether the active pet actually has a photo file on this device — the
    /// "Include photo" toggle is only shown when true.</summary>
    private bool _hasPhoto;
    public bool HasPhoto { get => _hasPhoto; private set => SetProperty(ref _hasPhoto, value); }

    /// <summary>Opt-in: include the pet's profile photo in the report header. Default
    /// off (the report is data-minimized); reset every time the sheet opens.</summary>
    private bool _includePhoto;
    public bool IncludePhoto { get => _includePhoto; set => SetProperty(ref _includePhoto, value); }

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
    public ICommand ToggleIncludePhotoCommand { get; }
    public ICommand GenerateCommand { get; }
    public ICommand ViewCommand { get; }
    public ICommand ShareCommand { get; }

    private void Open()
    {
        _pet = _activePetService.ActivePet;

        // Fresh interaction every time — a previous export's result must not leak.
        _result = null;
        IsDone = false;
        IsGenerating = false;
        StatusMessage = string.Empty;
        SelectedDays = 90;

        // Photo opt-in defaults off every open; the toggle only shows when the pet has
        // a photo file present on this device.
        IncludePhoto = false;
        HasPhoto = _pet.PhotoFullPath is { } p && File.Exists(p);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        IsPresented = true;
    }

    private void SelectPeriod(string days)
    {
        if (int.TryParse(days, out var parsed) && parsed > 0)
            SelectedDays = parsed;
    }

    private async Task GenerateAsync()
    {
        if (IsGenerating)
            return;
        IsGenerating = true;
        StatusMessage = string.Empty;

        try
        {
#pragma warning disable CS0162 // unreachable branch — intentional compile-time switch
            if (UseSampleReportData)
                _result = await _reports.GenerateSampleAsync();
            else
                _result = _pet.Id == 0
                    ? null // no active pet behaves like "no data"
                    : await _reports.GenerateAsync(
                        _pet.Id, DateTime.Today.AddDays(-SelectedDays), DateTime.Today, IncludePhoto);
#pragma warning restore CS0162

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
