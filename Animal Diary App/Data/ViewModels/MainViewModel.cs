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
    public WaterSheetViewModel WaterSheetVM { get; }

    /// <summary>The Constellation — the pet's history as a night sky (read-only;
    /// see Data/Models/CelestialEvent.cs for the one rule it obeys).</summary>
    public ConstellationViewModel ConstellationVM { get; }

    /// <summary>Today's stat-card picker — "what matters most today?" (see
    /// Data/Models/TodayCards.cs).</summary>
    public TodayCardSheetViewModel TodayCardSheetVM { get; }

    // ── Reusable condition-setup sheets ("one menu, two doors": onboarding + Manage) ──
    public DiabetesSetupSheetViewModel DiabetesSetupVM { get; }
    public CkdSetupSheetViewModel CkdSetupVM { get; }
    public EpilepsySetupSheetViewModel EpilepsySetupVM { get; }

    /// <summary>The one create/edit sheet behind every tracker the owner defines.</summary>
    public CustomTrackerSheetViewModel CustomTrackerVM { get; }

    /// <summary>The Journal's logging sheet for an owner-defined tracker.</summary>
    public CustomEntrySheetViewModel CustomEntrySheetVM { get; }

    /// <summary>The Manage Pet page (identity, conditions, care plan, medications).</summary>
    public ManagePetViewModel ManageVM { get; }

    /// <summary>The Settings → Cloud Features sheet (account + backup).</summary>
    public CloudSheetViewModel CloudVM { get; }

    /// <summary>The cloud sync boundary — pages subscribe to its
    /// RemoteChangesApplied so the visible page reloads when another
    /// device/caregiver's changes land (mirrors how Analytics is exposed).</summary>
    public Animal_Diary_App.Data.Services.Cloud.ICloudSyncService CloudSync { get; }

    /// <summary>The Manage-pet "Pet sharing" sheet (invites, members, leave).</summary>
    public SharingSheetViewModel SharingVM { get; }

    /// <summary>The monetization boundary — the single gate every add/edit surface
    /// checks. Exposed here so hand-built pages reach it through the shared VM, like
    /// <see cref="Analytics"/> and <see cref="CloudSync"/>.</summary>
    public Animal_Diary_App.Data.Services.Billing.IEntitlementService Entitlements { get; }

    /// <summary>The gate for the pet-scoped surfaces (Journal, Manage, Medications): true
    /// when you may write to the pet currently being looked at. Differs from
    /// <c>Entitlements.HasFullAccess</c> for a <b>caregiver on someone else's pet</b>, who
    /// is covered by that owner's subscription or trial.
    ///
    /// <para>Read it per action, never cache it: the active pet changes under the page,
    /// and sponsorship can end mid-session when a sync lands.</para></summary>
    public bool CanEditActivePet => Entitlements.CanEditPet(PetVM.ActivePet?.SyncId);

    /// <summary>The subscribe sheet (yearly + monthly + restore).</summary>
    public SubscribeSheetViewModel SubscribeVM { get; }

    /// <summary>The reusable trial-message sheet (explainer / pre-end nudge / read-only).</summary>
    public TrialMessageViewModel TrialMessageVM { get; }

    /// <summary>The access-code sheet, opened from Settings only.</summary>
    public RedeemCodeSheetViewModel RedeemVM { get; }

    /// <summary>The Care page's "feedback or a problem" sheet (Discord / direct email).</summary>
    public FeedbackSheetViewModel FeedbackVM { get; }

    /// <summary>The shared multi-choice confirmation sheet — anything with more than two
    /// outcomes (reset scope, remove-pet, sign-out). Two-outcome confirms stay native.</summary>
    public ConfirmSheetViewModel ConfirmVM { get; }

    /// <summary>The crop-and-rotate sheet a photo passes through on its way to becoming a
    /// pet's avatar — hosted by the create/edit pet page, which owns the media picker.</summary>
    public PhotoEditorSheetViewModel PhotoEditorVM { get; }

    /// <summary>The hidden developer diagnostics sheet (Settings → "Code").</summary>
    public DevSheetViewModel DevVM { get; }

    /// <summary>The AI entry importer, reached from the dev sheet behind its own code.</summary>
    public ImportViewModel ImportVM { get; }

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
 ConstellationViewModel constellationVM,
 GlucoseSheetViewModel glucoseSheetVM,
 MoodSheetViewModel moodSheetVM,
 WeightSheetViewModel weightSheetVM,
 AppetiteSheetViewModel appetiteSheetVM,
 SeizureSheetViewModel seizureSheetVM,
 WaterSheetViewModel waterSheetVM,
 TodayCardSheetViewModel todayCardSheetVM,
 DiabetesSetupSheetViewModel diabetesSetupVM,
 CkdSetupSheetViewModel ckdSetupVM,
 EpilepsySetupSheetViewModel epilepsySetupVM,
        CustomTrackerSheetViewModel customTrackerVM,
        CustomEntrySheetViewModel customEntrySheetVM,
 ManagePetViewModel manageVM,
 ExportSheetViewModel exportSheetVM,
 ReportPreviewViewModel reportPreviewVM,
 DocumentsViewModel documentsVM,
 CloudSheetViewModel cloudVM,
 SharingSheetViewModel sharingVM,
 SubscribeSheetViewModel subscribeVM,
 TrialMessageViewModel trialMessageVM,
 RedeemCodeSheetViewModel redeemVM,
 FeedbackSheetViewModel feedbackVM,
 ConfirmSheetViewModel confirmVM,
 PhotoEditorSheetViewModel photoEditorVM,
 Animal_Diary_App.Data.Services.Billing.IEntitlementService entitlements,
 DevSheetViewModel devVM,
 ImportViewModel importVM,
 Animal_Diary_App.Data.Services.Cloud.ICloudSyncService cloudSync,
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
        ConstellationVM = constellationVM;
        GlucoseSheetVM = glucoseSheetVM;
        MoodSheetVM = moodSheetVM;
        WeightSheetVM = weightSheetVM;
        AppetiteSheetVM = appetiteSheetVM;
        SeizureSheetVM = seizureSheetVM;
        WaterSheetVM = waterSheetVM;
        TodayCardSheetVM = todayCardSheetVM;
        DiabetesSetupVM = diabetesSetupVM;
        CkdSetupVM = ckdSetupVM;
        EpilepsySetupVM = epilepsySetupVM;
        CustomTrackerVM = customTrackerVM;
        CustomEntrySheetVM = customEntrySheetVM;
        ManageVM = manageVM;
        ExportSheetVM = exportSheetVM;
        ReportPreviewVM = reportPreviewVM;
        DocumentsVM = documentsVM;
        CloudVM = cloudVM;
        SharingVM = sharingVM;
        SubscribeVM = subscribeVM;
        TrialMessageVM = trialMessageVM;
        RedeemVM = redeemVM;
        FeedbackVM = feedbackVM;
        ConfirmVM = confirmVM;
        PhotoEditorVM = photoEditorVM;
        Entitlements = entitlements;
        DevVM = devVM;
        ImportVM = importVM;
        CloudSync = cloudSync;

        _draftViewModels = new IResettableDraft[] { PetVM, MedicationVM, CloudVM, SubscribeVM, RedeemVM, DevVM, ImportVM };
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
            MainPageVM.LoadLatestWeightAsync(),
            CalendarVM.PrepareDataAsync());
    }
}