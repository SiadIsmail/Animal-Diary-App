namespace Animal_Diary_App.Data.ViewModels;

using System.Text;
using System.Windows.Input;
using Animal_Diary_App.Data.Services.Cloud;

/// <summary>
/// A hidden developer panel, reached from Settings → "Code" and unlocked with a
/// gate code. It surfaces the cloud/auth state and the recent event log that is
/// otherwise swallowed to Debug — so silent sign-in drops, session expiries, and
/// sync errors become visible and copyable. Read-only diagnostics plus a couple
/// of manual triggers; it stores and shows nothing user-facing, so its content
/// is deliberately English-only (not a localized feature).
/// </summary>
public class DevSheetViewModel : BaseViewModel, IResettableDraft
{
    // The gate code. Obscure by intent — this is a developer affordance, not a
    // security boundary (the panel only ever shows coarse technical logs).
    private const string GateCode = "Sewr";

    private readonly ICloudAuthService _auth;
    private readonly ICloudSyncService _sync;

    public DevSheetViewModel(ICloudAuthService auth, ICloudSyncService sync)
    {
        _auth = auth;
        _sync = sync;

        OpenCommand = new Command(() => { Reset(); IsPresented = true; });
        DismissCommand = new Command(() => IsPresented = false);
        UnlockCommand = new Command(Unlock);
        RefreshCommand = new Command(RefreshState);
        CopyCommand = new Command(async () => await CopyAsync());
        ClearLogCommand = new Command(() => { CloudDiagnostics.Clear(); RefreshState(); });
        SyncNowCommand = new Command(async () => { await _sync.SyncNowAsync(); RefreshState(); });
        ForceRefreshCommand = new Command(async () => { await _auth.GetSessionAsync(forceRefresh: true); RefreshState(); });
    }

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    public string Title => "Developer";
    public string Subtitle => _unlocked ? "Cloud diagnostics" : "Enter code";

    private bool _unlocked;
    public bool IsLocked => !_unlocked;
    public bool IsUnlocked => _unlocked;

    private string _codeInput = string.Empty;
    public string CodeInput { get => _codeInput; set => SetProperty(ref _codeInput, value); }

    private string _authState = string.Empty;
    public string AuthState { get => _authState; set => SetProperty(ref _authState, value); }

    private string _log = string.Empty;
    public string Log { get => _log; set => SetProperty(ref _log, value); }

    public ICommand OpenCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand UnlockCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand ForceRefreshCommand { get; }

    private void Unlock()
    {
        if (CodeInput.Trim() != GateCode)
        {
            AuthState = "Wrong code.";
            OnPropertyChanged(nameof(AuthState));
            return;
        }
        _unlocked = true;
        CodeInput = string.Empty;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(Subtitle));
        RefreshState();
    }

    private void RefreshState()
    {
        AuthState = BuildAuthState();
        Log = string.Join("\n", CloudDiagnostics.Snapshot());
    }

    private string BuildAuthState()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Signed in : {_auth.IsSignedIn}");
        sb.AppendLine($"Email     : {_auth.Email ?? "—"}");
        var uid = _auth.UserId;
        sb.AppendLine($"User id   : {(string.IsNullOrEmpty(uid) ? "—" : uid)}");

        if (_auth.SessionExpiresUtc is DateTime exp)
        {
            var remaining = exp - DateTime.UtcNow;
            var mins = (int)Math.Round(remaining.TotalMinutes);
            sb.AppendLine($"Token exp : {exp:yyyy-MM-dd HH:mm:ss}Z ({(mins >= 0 ? mins + " min left" : "EXPIRED " + (-mins) + " min ago")})");
        }
        else
        {
            sb.AppendLine("Token exp : —");
        }

        sb.AppendLine($"Backup on : {_sync.IsBackupEnabled}");
        sb.AppendLine($"Last sync : {(_sync.LastSyncedUtc is DateTime ls ? ls.ToLocalTime().ToString("g") : "never")}");
        return sb.ToString().TrimEnd();
    }

    private async Task CopyAsync()
    {
        var payload = $"=== Felova cloud diagnostics ===\n{AuthState}\n\n--- log (newest first) ---\n{Log}";
        try { await Clipboard.Default.SetTextAsync(payload); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dev] copy failed: {ex.Message}"); }
    }

    private void Reset()
    {
        _unlocked = false;
        CodeInput = string.Empty;
        AuthState = string.Empty;
        Log = string.Empty;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(Subtitle));
    }

    public void ResetDraft()
    {
        IsPresented = false;
        Reset();
    }
}
