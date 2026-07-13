using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

namespace Animal_Diary_App.Data.ViewModels;

public class MainViewModel
{
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

    // ── Reusable condition-setup sheets ("one menu, two doors": onboarding + Manage) ──
    public DiabetesSetupSheetViewModel DiabetesSetupVM { get; }
    public CkdSetupSheetViewModel CkdSetupVM { get; }
    public EpilepsySetupSheetViewModel EpilepsySetupVM { get; }

    /// <summary>The Manage Pet page (identity, conditions, care plan, medications).</summary>
    public ManagePetViewModel ManageVM { get; }

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
 DiabetesSetupSheetViewModel diabetesSetupVM,
 CkdSetupSheetViewModel ckdSetupVM,
 EpilepsySetupSheetViewModel epilepsySetupVM,
 ManagePetViewModel manageVM)
    {
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
        DiabetesSetupVM = diabetesSetupVM;
        CkdSetupVM = ckdSetupVM;
        EpilepsySetupVM = epilepsySetupVM;
        ManageVM = manageVM;

        _draftViewModels = new IResettableDraft[] { PetVM, MedicationVM };
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