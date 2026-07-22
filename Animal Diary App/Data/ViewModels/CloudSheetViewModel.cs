namespace Animal_Diary_App.Data.ViewModels;

using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Helpers;

/// <summary>
/// The Settings → Cloud Features sheet: one FelovaBottomSheet whose body swaps
/// between the signed-out pitch, the sign-in/sign-up forms, the 6-digit code
/// step, password recovery, and the signed-in status card. All auth/sync work
/// goes through the cloud boundary (<see cref="ICloudAuthService"/> /
/// <see cref="ICloudSyncService"/>); this VM only holds form state.
/// </summary>
public class CloudSheetViewModel : BaseViewModel, IResettableDraft
{
    private enum Mode { Intro, SignIn, SignUp, VerifyCode, ResetRequest, ResetVerify, SignedIn }

    private readonly ICloudAuthService _auth;
    private readonly ICloudSyncService _sync;
    private readonly ICloudSharingService _sharing;
    private Mode _mode = Mode.Intro;

    public CloudSheetViewModel(ICloudAuthService auth, ICloudSyncService sync, ICloudSharingService sharing)
    {
        _auth = auth;
        _sync = sync;
        _sharing = sharing;

        OpenCommand = new Command(async () => await OpenAsync());
        DismissCommand = new Command(() => IsPresented = false);
        GoSignUpCommand = new Command(() => SetMode(Mode.SignUp));
        GoSignInCommand = new Command(() => SetMode(Mode.SignIn));
        GoForgotCommand = new Command(() => SetMode(Mode.ResetRequest));
        SubmitCommand = new Command(async () => await SubmitAsync());
        ResendCommand = new Command(async () => await ResendAsync());
        EnableBackupCommand = new Command(async () => await EnableBackupAsync());
        SyncNowCommand = new Command(async () => await SyncNowAsync());
        SignOutCommand = new Command(async () => await SignOutAsync());
        DeleteAccountCommand = new Command(async () => await DeleteAccountAsync());
        JoinCommand = new Command(async () => await JoinAsync());
        ContinueWithGoogleCommand = new Command(async () => await GoogleAsync());

        // Session/sync state can change underneath the sheet (expiry, background
        // sync finishing) — re-render, marshalled to the UI thread.
        _auth.SessionChanged += () => MainThread.BeginInvokeOnMainThread(RefreshStateFromServices);
        _sync.StateChanged += () => MainThread.BeginInvokeOnMainThread(RefreshStateFromServices);
    }

    // ── sheet shell ─────────────────────────────────────────────────────────

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    public string Title => Loc("Cloud_Title");
    public string Subtitle => _mode switch
    {
        Mode.SignedIn => LocalizationManager.Instance.Format("Cloud_SignedInAs", _auth.Email ?? ""),
        Mode.VerifyCode or Mode.ResetVerify => Loc("Cloud_CodeTitle"),
        _ => Loc("Cloud_IntroSubtitle"),
    };

    // ── form state ──────────────────────────────────────────────────────────

    private string _email = string.Empty;
    public string Email { get => _email; set { SetProperty(ref _email, value); OnPropertyChanged(nameof(CanSubmit)); } }

    private string _password = string.Empty;
    public string Password { get => _password; set { SetProperty(ref _password, value); OnPropertyChanged(nameof(CanSubmit)); } }

    private string _code = string.Empty;
    public string Code { get => _code; set { SetProperty(ref _code, value); OnPropertyChanged(nameof(CanSubmit)); } }

    private string _newPassword = string.Empty;
    public string NewPassword { get => _newPassword; set { SetProperty(ref _newPassword, value); OnPropertyChanged(nameof(CanSubmit)); } }

    private string _errorText = string.Empty;
    public string ErrorText { get => _errorText; set => SetProperty(ref _errorText, value); }

    private string _infoText = string.Empty;
    public string InfoText { get => _infoText; set => SetProperty(ref _infoText, value); }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(CanSubmit)); OnPropertyChanged(nameof(CanJoin)); }
    }

    public bool CanSubmit => !IsBusy && _mode switch
    {
        Mode.SignIn or Mode.SignUp => Email.Contains('@') && Password.Length >= 6,
        Mode.VerifyCode => Code.Trim().Length >= 6,
        Mode.ResetRequest => Email.Contains('@'),
        Mode.ResetVerify => Code.Trim().Length >= 6 && NewPassword.Length >= 6,
        _ => false,
    };

    // ── mode visibility (compiled bindings want plain bools) ────────────────

    public bool IsIntroVisible => _mode == Mode.Intro;
    public bool IsCredentialsVisible => _mode is Mode.SignIn or Mode.SignUp;

    /// <summary>Google sign-in is offered on Android only — a social login on iOS
    /// obliges Sign in with Apple (App Store policy), deferred until iOS ships.
    /// Shown on the pre-account modes.</summary>
    public bool IsGoogleAvailable =>
        DeviceInfo.Platform == DevicePlatform.Android &&
        (_mode == Mode.Intro || _mode == Mode.SignIn || _mode == Mode.SignUp);
    public bool IsSignUpMode => _mode == Mode.SignUp;
    public bool IsSignInMode => _mode == Mode.SignIn;
    public bool IsVerifyVisible => _mode == Mode.VerifyCode;
    public bool IsResetRequestVisible => _mode == Mode.ResetRequest;
    public bool IsResetVerifyVisible => _mode == Mode.ResetVerify;
    public bool IsSignedInVisible => _mode == Mode.SignedIn;

    public string SubmitLabel => _mode switch
    {
        Mode.SignUp => Loc("Cloud_CreateAccount"),
        Mode.SignIn => Loc("Cloud_SignIn"),
        Mode.VerifyCode => Loc("Cloud_Verify"),
        Mode.ResetRequest => Loc("Cloud_SendCode"),
        Mode.ResetVerify => Loc("Cloud_ResetVerify"),
        _ => string.Empty,
    };

    // ── signed-in card state ────────────────────────────────────────────────

    public string SignedInEmail => _auth.Email ?? string.Empty;
    public bool IsBackupEnabled => _sync.IsBackupEnabled;
    public bool ShowEnablePrompt => _mode == Mode.SignedIn && !_sync.IsBackupEnabled;
    public string LastSyncedDisplay => _sync.LastSyncedUtc is DateTime utc
        ? LocalizationManager.Instance.Format("Cloud_LastSynced", utc.ToLocalTime().ToString("g"))
        : Loc("Cloud_NeverSynced");

    /// <summary>Set by the hosting page — native confirm before account deletion
    /// (same pattern as <see cref="SettingsViewModel.ConfirmDeleteAllData"/>).</summary>
    public Func<Task<bool>>? ConfirmDeleteAccount { get; set; }

    // ── join a shared pet (invite code) ─────────────────────────────────────

    private string _joinCode = string.Empty;
    public string JoinCode { get => _joinCode; set { SetProperty(ref _joinCode, value); OnPropertyChanged(nameof(CanJoin)); } }
    public bool CanJoin => !IsBusy && JoinCode.Trim().Length >= 8;

    // ── commands ────────────────────────────────────────────────────────────

    public ICommand OpenCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand GoSignUpCommand { get; }
    public ICommand GoSignInCommand { get; }
    public ICommand GoForgotCommand { get; }
    public ICommand SubmitCommand { get; }
    public ICommand ResendCommand { get; }
    public ICommand EnableBackupCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand SignOutCommand { get; }
    public ICommand DeleteAccountCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand ContinueWithGoogleCommand { get; }

    private async Task OpenAsync()
    {
        ErrorText = string.Empty;
        InfoText = string.Empty;
        // GetSessionAsync is the authoritative check (it refreshes / detects expiry).
        var session = await Guarded(() => _auth.GetSessionAsync());
        SetMode(session != null ? Mode.SignedIn : Mode.Intro);
        IsPresented = true;
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
            return;
        ErrorText = string.Empty;

        switch (_mode)
        {
            case Mode.SignUp:
                if (await Run(() => _auth.SignUpAsync(Email.Trim(), Password)))
                {
                    InfoText = LocalizationManager.Instance.Format("Cloud_CodeBody", Email.Trim());
                    SetMode(Mode.VerifyCode);
                }
                break;

            case Mode.SignIn:
                if (await Run(() => _auth.SignInAsync(Email.Trim(), Password)))
                    EnterSignedIn();
                break;

            case Mode.VerifyCode:
                if (await Run(() => _auth.VerifySignUpAsync(Email.Trim(), Code.Trim())))
                    EnterSignedIn();
                break;

            case Mode.ResetRequest:
                if (await Run(() => _auth.RequestPasswordResetAsync(Email.Trim())))
                {
                    InfoText = LocalizationManager.Instance.Format("Cloud_CodeBody", Email.Trim());
                    SetMode(Mode.ResetVerify);
                }
                break;

            case Mode.ResetVerify:
                if (await Run(() => _auth.VerifyPasswordResetAsync(Email.Trim(), Code.Trim(), NewPassword)))
                    EnterSignedIn();
                break;
        }
    }

    private async Task ResendAsync()
    {
        ErrorText = string.Empty;
        if (await Run(() => _mode == Mode.ResetVerify
                ? _auth.RequestPasswordResetAsync(Email.Trim())
                : _auth.ResendSignUpCodeAsync(Email.Trim())))
            InfoText = LocalizationManager.Instance.Format("Cloud_CodeBody", Email.Trim());
    }

    private async Task GoogleAsync()
    {
        ErrorText = string.Empty;
        if (await Run(() => _auth.SignInWithGoogleAsync()))
            EnterSignedIn();
    }

    private void EnterSignedIn()
    {
        Password = string.Empty;
        Code = string.Empty;
        NewPassword = string.Empty;
        InfoText = string.Empty;
        SetMode(Mode.SignedIn);
    }

    private async Task EnableBackupAsync()
    {
        ErrorText = string.Empty;
        await Run(async () =>
        {
            var outcome = await _sync.EnableBackupAsync();
            if (outcome is SyncOutcome.Failed)
                throw new CloudException(CloudErrorKind.Other, 0, "sync failed");
            if (outcome is SyncOutcome.Offline)
                InfoText = Loc("Cloud_EnabledOffline");
        });
        RefreshStateFromServices();
    }

    private async Task SyncNowAsync()
    {
        ErrorText = string.Empty;
        await Run(async () =>
        {
            var outcome = await _sync.SyncNowAsync();
            if (outcome == SyncOutcome.Offline)
                ErrorText = Loc("Cloud_ErrNetwork");
            else if (outcome is SyncOutcome.Failed or SyncOutcome.AuthExpired)
                ErrorText = Loc("Cloud_ErrGeneric");
        });
        RefreshStateFromServices();
    }

    private async Task SignOutAsync()
    {
        await Run(() => _auth.SignOutAsync());
        SetMode(Mode.Intro);
    }

    private async Task JoinAsync()
    {
        if (!CanJoin)
            return;
        ErrorText = string.Empty;
        if (await Run(() => _sharing.RedeemInviteAsync(JoinCode)))
        {
            JoinCode = string.Empty;
            InfoText = Loc("Cloud_JoinSuccess");
            // The membership exists server-side; the sync's membership+pull
            // brings the pet's data down. Fire-and-forget — status shows in-sheet.
            _ = _sync.SyncNowAsync();
        }
    }

    private async Task DeleteAccountAsync()
    {
        if (ConfirmDeleteAccount != null && !await ConfirmDeleteAccount())
            return;
        ErrorText = string.Empty;
        if (await Run(() => _sync.DeleteAccountAsync()))
            SetMode(Mode.Intro);
    }

    // ── plumbing ────────────────────────────────────────────────────────────

    /// <summary>Run a cloud call with busy state + localized error mapping;
    /// true = it completed.</summary>
    private async Task<bool> Run(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException)
        {
            // The user backed out of the Google browser flow — not an error.
            return false;
        }
        catch (CloudException ex)
        {
            ErrorText = Loc(ex.Kind switch
            {
                CloudErrorKind.Network => "Cloud_ErrNetwork",
                CloudErrorKind.InvalidCredentials => "Cloud_ErrCredentials",
                CloudErrorKind.EmailTaken => "Cloud_ErrEmailTaken",
                CloudErrorKind.EmailNotConfirmed => "Cloud_ErrNotConfirmed",
                CloudErrorKind.InvalidCode => "Cloud_ErrCode",
                CloudErrorKind.WeakPassword => "Cloud_ErrWeakPassword",
                CloudErrorKind.RateLimited => "Cloud_ErrRateLimited",
                CloudErrorKind.InviteInvalid => "Cloud_ErrInviteInvalid",
                CloudErrorKind.InviteAlreadyMember => "Cloud_ErrAlreadyMember",
                _ => "Cloud_ErrGeneric",
            });
            Debug.WriteLine($"[Cloud] {ex.Kind} ({ex.StatusCode}): {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            ErrorText = Loc("Cloud_ErrGeneric");
            Debug.WriteLine($"[Cloud] unexpected: {ex}");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<T?> Guarded<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception ex) { Debug.WriteLine($"[Cloud] {ex.Message}"); return default; }
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;
        ErrorText = string.Empty;
        RefreshStateFromServices();
    }

    private void RefreshStateFromServices()
    {
        OnPropertyChanged(nameof(IsIntroVisible));
        OnPropertyChanged(nameof(IsCredentialsVisible));
        OnPropertyChanged(nameof(IsGoogleAvailable));
        OnPropertyChanged(nameof(IsSignUpMode));
        OnPropertyChanged(nameof(IsSignInMode));
        OnPropertyChanged(nameof(IsVerifyVisible));
        OnPropertyChanged(nameof(IsResetRequestVisible));
        OnPropertyChanged(nameof(IsResetVerifyVisible));
        OnPropertyChanged(nameof(IsSignedInVisible));
        OnPropertyChanged(nameof(SubmitLabel));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SignedInEmail));
        OnPropertyChanged(nameof(IsBackupEnabled));
        OnPropertyChanged(nameof(ShowEnablePrompt));
        OnPropertyChanged(nameof(LastSyncedDisplay));
        OnPropertyChanged(nameof(SettingsRowSubtitle));
    }

    /// <summary>Subtitle for the Settings-panel row: signed-in email or the
    /// private-by-default reassurance.</summary>
    public string SettingsRowSubtitle => _auth.IsSignedIn
        ? LocalizationManager.Instance.Format("Cloud_SignedInAs", _auth.Email ?? "")
        : Loc("Cloud_RowSubtitleOff");

    private static string Loc(string key) => LocalizationManager.Instance.GetString(key);

    public void ResetDraft()
    {
        Email = Password = Code = NewPassword = string.Empty;
        ErrorText = InfoText = string.Empty;
        IsPresented = false;
        SetMode(Mode.Intro);
    }
}
