namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;

/// <summary>
/// Shared base for the reusable condition-setup sheets (Diabetes, CKD, Epilepsy).
/// Each is a <see cref="Controls.FelovaBottomSheet"/> body whose job is to answer
/// "what will Felova add for this condition?" — configuration, not disease education.
///
/// The SAME sheet instance is hosted by two doors: the onboarding condition picker
/// and the Manage Pet page. Saving writes to the ACTIVE pet — it links the condition
/// (<see cref="PetConditionService"/>) and writes/updates the condition's trackers
/// (<see cref="TrackerService"/>), then raises <see cref="Saved"/> so the host can
/// mark the row configured / refresh. Presentation state only; the medical shape of
/// the plan lives in the catalog + services.
/// </summary>
public abstract class ConditionSetupSheetViewModel : BaseViewModel
{
    protected readonly ActivePetService ActivePet;
    protected readonly PetConditionService Conditions;
    protected readonly TrackerService Trackers;

    protected ConditionSetupSheetViewModel(
        ActivePetService activePet,
        PetConditionService conditions,
        TrackerService trackers)
    {
        ActivePet = activePet;
        Conditions = conditions;
        Trackers = trackers;

        SaveCommand = new Command(async () => await SaveAsync());
        DismissCommand = new Command(() => IsPresented = false);
    }

    /// <summary>The condition id this sheet configures (see <see cref="ConditionCatalog"/>).</summary>
    public abstract string ConditionId { get; }

    /// <summary>Raised after the condition + its trackers are persisted to the active
    /// pet, so the host can flip the picker row to "configured" or refresh Manage.</summary>
    public event Action? Saved;

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    /// <summary>Serif sheet title (localized) — the condition's name.</summary>
    public abstract string TitleText { get; }

    /// <summary>Caveat subtitle (localized) — a short, action-oriented framing.</summary>
    public abstract string SubtitleText { get; }

    public ICommand SaveCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>Pre-fill the controls from the pet's current plan (so reopening an
    /// already-configured condition edits it) and present.</summary>
    public async Task OpenAsync()
    {
        var pet = ActivePet.ActivePet;
        if (pet != null && pet.Id != 0)
            await LoadAsync(pet.Id);

        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(SubtitleText));
        IsPresented = true;
    }

    /// <summary>Subclass hook: load current tracker settings into the sheet's controls.
    /// Defaults to no-op for setup sheets with nothing to configure.</summary>
    protected virtual Task LoadAsync(int petId) => Task.CompletedTask;

    /// <summary>Subclass hook: validate the inputs BEFORE anything is written, so an
    /// invalid form (e.g. a half-filled range) keeps the sheet open and nothing is
    /// persisted. Set an error message here. Defaults to always-valid.</summary>
    protected virtual bool Validate() => true;

    /// <summary>Subclass hook: write this condition's trackers for the pet. The base
    /// has already linked the condition and guaranteed the always-on defaults exist,
    /// and <see cref="Validate"/> has passed.</summary>
    protected abstract Task PersistAsync(int petId);

    private async Task SaveAsync()
    {
        var pet = ActivePet.ActivePet;
        if (pet == null || pet.Id == 0)
            return;

        // Validate before touching storage so a rejected form leaves no partial state.
        if (!Validate())
            return;

        // Link the condition, guarantee the always-on defaults (Mood + Weight) exist,
        // then let the subclass write its condition-specific trackers on top.
        await Conditions.AddAsync(pet.Id, ConditionId);
        await Trackers.EnsureSeededAsync(pet.Id, System.Array.Empty<string>());
        await PersistAsync(pet.Id);

        IsPresented = false;
        Saved?.Invoke();
    }
}
