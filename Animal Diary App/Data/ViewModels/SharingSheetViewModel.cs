namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Helpers;

/// <summary>One row of the sharing sheet's member list.</summary>
public class MemberRow
{
    public MemberRow(PetMemberInfo info, bool viewerIsOwner)
    {
        UserId = info.UserId;
        Email = info.Email;
        RoleDisplay = LocalizationManager.Instance.GetString(info.IsOwner ? "Cloud_RoleOwner" : "Cloud_RoleCaregiver");
        // Only the owner removes people, never themselves here — their own exit
        // is the pet-deletion path, and a caregiver's is the Leave button below.
        CanRemove = viewerIsOwner && !info.IsOwner && !info.IsMe;
        IsMe = info.IsMe;
    }

    public string UserId { get; }
    public string Email { get; }
    public string RoleDisplay { get; }
    public bool CanRemove { get; }
    public bool IsMe { get; }
}

/// <summary>
/// The Manage-pet "Pet sharing" sheet: member list plus, for the owner, invite
/// minting/sharing — for a caregiver, the Leave action. All rules live
/// server-side (0006's RPCs); this VM only renders outcomes. Operates on the
/// ACTIVE pet, like the rest of the Manage page.
/// </summary>
public class SharingSheetViewModel : BaseViewModel
{
    private readonly ICloudSharingService _sharing;
    private readonly ICloudSyncService _sync;
    private readonly ICloudAuthService _auth;
    private readonly ActivePetService _activePet;

    public SharingSheetViewModel(
        ICloudSharingService sharing,
        ICloudSyncService sync,
        ICloudAuthService auth,
        ActivePetService activePet)
    {
        _sharing = sharing;
        _sync = sync;
        _auth = auth;
        _activePet = activePet;

        OpenCommand = new Command(async () => await OpenAsync());
        DismissCommand = new Command(() => IsPresented = false);
        CreateInviteCommand = new Command(async () => await CreateInviteAsync());
        ShareCodeCommand = new Command(async () => await ShareCodeAsync());
        RemoveMemberCommand = new Command<MemberRow>(async row => await RemoveMemberAsync(row));
        LeaveCommand = new Command(async () => await LeaveAsync());

        _sync.StateChanged += () => MainThread.BeginInvokeOnMainThread(
            () => OnPropertyChanged(nameof(IsSharingAvailable)));
        _auth.SessionChanged += () => MainThread.BeginInvokeOnMainThread(
            () => OnPropertyChanged(nameof(IsSharingAvailable)));
    }

    // ── sheet shell ─────────────────────────────────────────────────────────

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => Loc("Cloud_SharingSheetTitle");
    public string Subtitle => LocalizationManager.Instance.Format(
        "Cloud_SharingSubtitle", _activePet.ActivePet?.Name ?? "");

    /// <summary>Drives the Manage page's Sharing row: only meaningful once the
    /// owner is signed in with backup on.</summary>
    public bool IsSharingAvailable => _auth.IsSignedIn && _sync.IsBackupEnabled;

    // ── state ───────────────────────────────────────────────────────────────

    public ObservableCollection<MemberRow> Members { get; } = new();

    private bool _isOwner;
    public bool IsOwner { get => _isOwner; set { SetProperty(ref _isOwner, value); OnPropertyChanged(nameof(IsCaregiver)); } }
    public bool IsCaregiver => !_isOwner && !_notSyncedYet;

    private bool _notSyncedYet;
    public bool NotSyncedYet { get => _notSyncedYet; set { SetProperty(ref _notSyncedYet, value); OnPropertyChanged(nameof(IsCaregiver)); } }

    private bool _hasMembers;
    public bool HasMembers { get => _hasMembers; set => SetProperty(ref _hasMembers, value); }
    public bool ShowEmptyHint => !_hasMembers && !_notSyncedYet;

    private string _inviteCode = string.Empty;
    public string InviteCode { get => _inviteCode; set { SetProperty(ref _inviteCode, value); OnPropertyChanged(nameof(HasInviteCode)); } }
    public bool HasInviteCode => !string.IsNullOrEmpty(_inviteCode);

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; set => SetProperty(ref _errorText, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    /// <summary>Set by the hosting page — native confirm before leaving (the leave
    /// purges the pet's data from this device; same pattern as delete-account).</summary>
    public Func<Task<bool>>? ConfirmLeave { get; set; }

    /// <summary>Raised after this user left the pet — the page pops back (the pet
    /// is about to vanish from the device).</summary>
    public event Action? LeftPet;

    // ── commands ────────────────────────────────────────────────────────────

    public ICommand OpenCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand CreateInviteCommand { get; }
    public ICommand ShareCodeCommand { get; }
    public ICommand RemoveMemberCommand { get; }
    public ICommand LeaveCommand { get; }

    private async Task OpenAsync()
    {
        ErrorText = string.Empty;
        InviteCode = string.Empty;
        Members.Clear();
        HasMembers = false;

        var pet = _activePet.ActivePet;
        var role = pet == null || string.IsNullOrEmpty(pet.SyncId) ? null : _sync.GetPetRole(pet.SyncId);

        // No cloud role yet = the pet hasn't completed a sync (or backup just
        // turned on) — surface that instead of a confusing server error.
        NotSyncedYet = role == null;
        IsOwner = role == "owner";
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(ShowEmptyHint));
        IsPresented = true;

        if (role != null && pet != null)
            await LoadMembersAsync(pet.SyncId);
    }

    private async Task LoadMembersAsync(string petSyncId)
    {
        await Run(async () =>
        {
            var members = await _sharing.GetMembersAsync(petSyncId);
            Members.Clear();
            foreach (var m in members)
                Members.Add(new MemberRow(m, IsOwner));
            // "No caregivers yet" means nobody BESIDES the owner.
            HasMembers = members.Count > 1;
            OnPropertyChanged(nameof(ShowEmptyHint));
        });
    }

    private async Task CreateInviteAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || string.IsNullOrEmpty(pet.SyncId))
            return;
        await Run(async () => InviteCode = await _sharing.CreateInviteAsync(pet.SyncId));
    }

    private async Task ShareCodeAsync()
    {
        if (!HasInviteCode)
            return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = LocalizationManager.Instance.Format("Cloud_ShareCodeText",
                    _activePet.ActivePet?.Name ?? "", InviteCode),
                Title = Loc("Cloud_SharingSheetTitle")
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Cloud] share failed: {ex.Message}");
        }
    }

    private async Task RemoveMemberAsync(MemberRow? row)
    {
        var pet = _activePet.ActivePet;
        if (row == null || pet == null || !row.CanRemove)
            return;
        if (await Run(() => _sharing.RemoveMemberAsync(pet.SyncId, row.UserId)))
            await LoadMembersAsync(pet.SyncId);
    }

    private async Task LeaveAsync()
    {
        var pet = _activePet.ActivePet;
        if (pet == null || IsOwner)
            return;
        if (ConfirmLeave != null && !await ConfirmLeave())
            return;

        if (await Run(() => _sharing.RemoveMemberAsync(pet.SyncId, _auth.UserId ?? "")))
        {
            IsPresented = false;
            LeftPet?.Invoke();
            // The next cycle's membership diff purges the pet from this device.
            _ = _sync.SyncNowAsync();
        }
    }

    private async Task<bool> Run(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (CloudException ex)
        {
            ErrorText = Loc(ex.Kind switch
            {
                CloudErrorKind.Network => "Cloud_ErrNetwork",
                CloudErrorKind.RateLimited => "Cloud_ErrRateLimited",
                CloudErrorKind.AuthExpired => "Cloud_ErrGeneric",
                _ => "Cloud_ErrGeneric",
            });
            Debug.WriteLine($"[Cloud] sharing {ex.Kind} ({ex.StatusCode}): {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            ErrorText = Loc("Cloud_ErrGeneric");
            Debug.WriteLine($"[Cloud] sharing unexpected: {ex}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);
}
