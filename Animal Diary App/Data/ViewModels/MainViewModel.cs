using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;

namespace Animal_Diary_App.Data.ViewModels;

public class MainViewModel
{
    /// <summary>The analytics boundary, exposed here so pages that are constructed
    /// by hand (Welcome, ConditionPicker, Calendar) — not resolved from DI — can log
    /// events through the shared VM they already receive. DI-resolved VMs inject
    /// <see cref="IAnalyticsService"/> directly instead.</summary>
    public IAnalyticsService Analytics { get; }

    public CalendarViewModel CalendarVM { get; }
    public MainPageViewModel MainPageVM { get; }
    public PetViewModel PetVM { get; }
    public MedicationViewModel MedicationVM { get; }
    public SettingsViewModel SettingsVM { get; }
    public ConditionPickerViewModel ConditionVM { get; }

    // ── Journal rework surfaces (new VMs; the CalendarViewModel is not reshaped) ──
    /// <summary>The "Still to do" chip row + glucose/appetite timeline + add-anything.</summary>
    public JournalLogViewModel JournalVM { get; }
    public GlucoseSheetViewModel GlucoseSheetVM { get; }
    public MoodSheetViewModel MoodSheetVM { get; }
    public WeightSheetViewModel WeightSheetVM { get; }
    public AppetiteSheetViewModel AppetiteSheetVM { get; }
    public SeizureSheetViewModel SeizureSheetVM { get; }

    // ── Reusable condition-setup sheets ("one menu, two doors": onboarding + Manage) ──
    public DiabetesSetupSheetViewModel DiabetesSetupVM { get; }
    public CkdSetupSheetViewModel CkdSetupVM { get; }
    public EpilepsySetupSheetViewModel EpilepsySetupVM { get; }

    /// <summary>The Manage Pet page (identity, conditions, care plan, medications).</summary>
    public ManagePetViewModel ManageVM { get; }

    /// <summary>The Settings → Cloud Features sheet (account + backup).</summary>
    public CloudSheetViewModel CloudVM { get; }

    // ── Vet-report surfaces (export sheet on Pets, preview page, Documents page) ──
    public ExportSheetViewModel ExportSheetVM { get; }
    public ReportPreviewViewModel ReportPreviewVM { get; }
    public DocumentsViewModel DocumentsVM { get; }

    // Child VMs that hold transient form/draft state, cleared together on a
    // global data reset. New draft forms just implement IResettableDraft and
    // get added here.
    private readonly IReadOnlyList<IResettableDraft> _draftViewModels;

    public MainViewModel(
 CalendarViewModel calendarVM,
 MainPageViewModel mainPageVM,
 PetViewModel petVM,
 MedicationViewModel medicationVM,
 SettingsViewModel settingsVM,
 ConditionPickerViewModel conditionVM,
 JournalLogViewModel journalVM,
 GlucoseSheetViewModel glucoseSheetVM,
 MoodSheetViewModel moodSheetVM,
 WeightSheetViewModel weightSheetVM,
 AppetiteSheetViewModel appetiteSheetVM,
 SeizureSheetViewModel seizureSheetVM,
 DiabetesSetupSheetViewModel diabetesSetupVM,
 CkdSetupSheetViewModel ckdSetupVM,
 EpilepsySetupSheetViewModel epilepsySetupVM,
 ManagePetViewModel manageVM,
 ExportSheetViewModel exportSheetVM,
 ReportPreviewViewModel reportPreviewVM,
 DocumentsViewModel documentsVM,
 CloudSheetViewModel cloudVM,
 IAnalyticsService analytics)
    {
        Analytics = analytics;
        MainPageVM = mainPageVM;
        PetVM = petVM;
        MedicationVM = medicationVM;
        CalendarVM = calendarVM;
        SettingsVM = settingsVM;
        ConditionVM = conditionVM;
        JournalVM = journalVM;
        GlucoseSheetVM = glucoseSheetVM;
        MoodSheetVM = moodSheetVM;
        WeightSheetVM = weightSheetVM;
        AppetiteSheetVM = appetiteSheetVM;
        SeizureSheetVM = seizureSheetVM;
        DiabetesSetupVM = diabetesSetupVM;
        CkdSetupVM = ckdSetupVM;
        EpilepsySetupVM = epilepsySetupVM;
        ManageVM = manageVM;
        ExportSheetVM = exportSheetVM;
        ReportPreviewVM = reportPreviewVM;
        DocumentsVM = documentsVM;
        CloudVM = cloudVM;

        _draftViewModels = new IResettableDraft[] { PetVM, MedicationVM, CloudVM };
    }

    /// <summary>
    /// Clears every form's in-memory draft. Called after a global data reset so
    /// stale inputs don't survive the wipe (the ViewModels are singletons, so
    /// their draft state would otherwise persist across the reset).
    /// </summary>
    public void ResetDrafts()
    {
        foreach (var draft in _draftViewModels)
            draft.ResetDraft();
    }
    public async Task LoadAsync()
    {
        await PetVM.LoadPetsAsync();
        await Task.WhenAll(
        //    MainPageVM.LoadCurrentPet(),
            MainPageVM.LoadLatestWeightAsync(),
            CalendarVM.PrepareDataAsync()
            );
    }
}