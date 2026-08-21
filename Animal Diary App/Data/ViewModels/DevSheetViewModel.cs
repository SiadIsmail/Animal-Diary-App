namespace Animal_Diary_App.Data.ViewModels;

using System.Text;
using System.Windows.Input;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.Services.Demo;

/// <summary>
/// A hidden developer panel, reached from Settings → "Code" and unlocked with a
/// gate code. It surfaces the cloud/auth state and the recent event log that is
/// otherwise swallowed to Debug, so silent sign-in drops, session expiries, and
/// sync errors become visible and copyable. Read-only diagnostics plus a couple
/// of manual triggers; it stores and shows nothing user-facing, so its content
/// is deliberately English-only (not a localized feature).
/// </summary>
public class DevSheetViewModel : BaseViewModel, IResettableDraft
{
    // The gate code. Obscure by intent: this is a developer affordance, not a
    // security boundary (the panel only ever shows coarse technical logs).
    private const string GateCode = "Sewr";

    /// <summary>
    /// The code handed to creators. <b>Separate from <see cref="GateCode"/> on purpose:</b>
    /// it opens the demo section and nothing else, so giving it out does not also hand over
    /// the cloud/auth diagnostics panel.
    ///
    /// <para>It will leak (creators film themselves typing) and that is designed for
    /// rather than defended against. The worst a stranger can do with it is give themselves
    /// two extra pets they did not create, which sync nowhere (<c>Pet.IsDemo</c>) and which
    /// "Remove demo pets" deletes. Revocability would mean a server round trip on a panel
    /// that has to work on a plane.</para>
    /// </summary>
    private const string CreatorCode = "Nightsky";

    /// <summary>
    /// The code that opens the AI entry importer. <b>Its own code, separate from both of
    /// the above</b>, for the same reason they are separate from each other: the importer
    /// WRITES to the diary, which neither of the others does, so reaching it must not be a
    /// side effect of handing a creator the demo pets.
    ///
    /// <para>Deliberately temporary and deliberately guessable. It exists so the feature
    /// can be tested on a real device without shipping it to anyone who has not been told
    /// it is there; it is not a security boundary, and it should be replaced by a real
    /// entry point (or removed) before this stops being an internal tool.</para>
    /// </summary>
    private const string ImportCode = "Import";

    private readonly ICloudAuthService _auth;
    private readonly ICloudSyncService _sync;
    private readonly DemoModeService _demo;

    public DevSheetViewModel(ICloudAuthService auth, ICloudSyncService sync, DemoModeService demo)
    {
        _auth = auth;
        _sync = sync;
        _demo = demo;

        OpenCommand = new Command(() => { Reset(); IsPresented = true; });
        DismissCommand = new Command(() => IsPresented = false);
        UnlockCommand = new Command(async () => await UnlockAsync());
        SeedDemoCommand = new Command(async () => await RunDemoAsync(
            async () => { var pet = await _demo.SeedAsync(); return pet is null ? "Nothing seeded." : $"Seeded. Active pet: {pet.Name}."; }));
        ClearDemoCommand = new Command(async () => await RunDemoAsync(
            async () => { var n = await _demo.ClearAsync(); return $"Removed {n} demo pet(s)."; }));
        OpenImportCommand = new Command(() => { IsPresented = false; ImportRequested?.Invoke(); });
        RefreshCommand = new Command(RefreshState);
        CopyCommand = new Command(async () => await CopyAsync());
        ClearLogCommand = new Command(() => { CloudDiagnostics.Clear(); RefreshState(); });

        // Both of these can throw (a rejected refresh raises CloudException), and a
        // `new Command(async () => …)` lambda is async void: an escaping exception
        // takes the process down. Guarded, and the failure lands in the very log this
        // panel exists to show.
        SyncNowCommand = new Command(async () => await RunDiagnosticAsync(
            () => _sync.SyncNowAsync(), "sync now"));
        ForceRefreshCommand = new Command(async () => await RunDiagnosticAsync(
            () => _auth.GetSessionAsync(forceRefresh: true), "force refresh"));
    }

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    /// <summary>What the entered code opened. Two levels, because the creator code is given
    /// out and the developer code is not.</summary>
    private enum Access { Locked, Creator, Developer, Import }

    private Access _access;

    public string Title => _access switch
    {
        Access.Creator => "Demo data",
        Access.Import => "Import",
        _ => "Developer",
    };

    public string Subtitle => _access switch
    {
        Access.Developer => "Cloud diagnostics",
        Access.Creator => "Seeded pets for filming",
        Access.Import => "Entries from an AI-written file",
        _ => "Enter code",
    };

    public bool IsLocked => _access == Access.Locked;

    /// <summary>The diagnostics half: developer code only.</summary>
    public bool IsUnlocked => _access == Access.Developer;

    /// <summary>The demo half. The creator and developer codes reach it: a developer needs
    /// the seeded pets as much as a creator does, and it is the fixture that replaced the
    /// compile-time switches. The import code does NOT: it opens one door.</summary>
    public bool ShowDemo => _access is Access.Creator or Access.Developer;

    /// <summary>The importer's door. The developer code reaches it too, so testing the
    /// feature does not mean signing out of the diagnostics panel first.</summary>
    public bool ShowImport => _access is Access.Import or Access.Developer;

    private string _codeInput = string.Empty;
    public string CodeInput { get => _codeInput; set => SetProperty(ref _codeInput, value); }

    private string _authState = string.Empty;
    public string AuthState { get => _authState; set => SetProperty(ref _authState, value); }

    private string _log = string.Empty;
    public string Log { get => _log; set => SetProperty(ref _log, value); }

    /// <summary>Asks the hosting page to push the importer. A ContentView cannot navigate,
    /// so the page that hosts this sheet does it: the same shape DocumentsViewModel uses
    /// for its preview push.</summary>
    public event Action? ImportRequested;

    public ICommand OpenCommand { get; }
    public ICommand OpenImportCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand UnlockCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SyncNowCommand { get; }
    public ICommand ForceRefreshCommand { get; }

    private async Task UnlockAsync()
    {
        var typed = CodeInput.Trim();

        _access = typed switch
        {
            GateCode => Access.Developer,
            CreatorCode => Access.Creator,
            ImportCode => Access.Import,
            _ => Access.Locked,
        };

        if (_access == Access.Locked)
        {
            AuthState = "Wrong code.";
            OnPropertyChanged(nameof(AuthState));
            return;
        }

        CodeInput = string.Empty;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(ShowDemo));
        OnPropertyChanged(nameof(ShowImport));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));

        if (_access == Access.Developer)
            RefreshState();

        await RefreshDemoStateAsync();
    }

    // ── The demo section ────────────────────────────────────────────────────────

    public ICommand SeedDemoCommand { get; }
    public ICommand ClearDemoCommand { get; }

    private string _demoState = string.Empty;
    public string DemoState { get => _demoState; private set => SetProperty(ref _demoState, value); }

    private bool _demoBusy;
    /// <summary>Seeding writes several thousand rows. Guarding re-entry matters more than
    /// usual here: a second tap mid-seed would run the "already seeded?" check before the
    /// first had set the flag, and produce two Kiras.</summary>
    public bool DemoBusy
    {
        get => _demoBusy;
        private set
        {
            if (SetProperty(ref _demoBusy, value))
                OnPropertyChanged(nameof(DemoIdle));
        }
    }

    public bool DemoIdle => !_demoBusy;

    private async Task RefreshDemoStateAsync()
    {
        try
        {
            var pets = await _demo.GetDemoPetsAsync();
            DemoState = pets.Count == 0
                ? "No demo pets on this device."
                : $"Demo pets: {string.Join(", ", pets.Select(p => p.Name))}";
        }
        catch (Exception ex)
        {
            DemoState = $"Could not read demo state: {ex.Message}";
        }
    }

    /// <summary>Run one demo action and always re-render, whatever happens. Same shape as
    /// <see cref="RunDiagnosticAsync"/>: a `new Command(async () => …)` lambda is async void,
    /// so an escaping exception takes the process down.</summary>
    private async Task RunDemoAsync(Func<Task<string>> action)
    {
        if (DemoBusy)
            return;

        DemoBusy = true;
        DemoState = "Working…";
        try
        {
            DemoState = await action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Demo] {ex}");
            DemoState = $"Failed: {ex.Message}";
        }
        finally
        {
            DemoBusy = false;
            await RefreshDemoStateAsync();
        }
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
        sb.AppendLine($"Email     : {_auth.Email ?? "-"}");
        var uid = _auth.UserId;
        sb.AppendLine($"User id   : {(string.IsNullOrEmpty(uid) ? "-" : uid)}");

        if (_auth.SessionExpiresUtc is DateTime exp)
        {
            var remaining = exp - DateTime.UtcNow;
            var mins = (int)Math.Round(remaining.TotalMinutes);
            sb.AppendLine($"Token exp : {exp:yyyy-MM-dd HH:mm:ss}Z ({(mins >= 0 ? mins + " min left" : "EXPIRED " + (-mins) + " min ago")})");
        }
        else
        {
            sb.AppendLine("Token exp : -");
        }

        sb.AppendLine($"Backup on : {_sync.IsBackupEnabled}");
        sb.AppendLine($"Last sync : {(_sync.LastSyncedUtc is DateTime ls ? ls.ToLocalTime().ToString("g") : "never")}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Run one manual diagnostic action and always re-render, whatever happens.
    /// A failure is recorded rather than thrown: this panel's whole job is to make silent
    /// cloud failures visible, so its own buttons must not become a new way to crash.</summary>
    private async Task RunDiagnosticAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            CloudDiagnostics.Record($"[Dev] {what} failed: {ex.Message}");
        }
        finally
        {
            RefreshState();
        }
    }

    private async Task CopyAsync()
    {
        var payload = $"=== Felova cloud diagnostics ===\n{AuthState}\n\n--- log (newest first) ---\n{Log}";
        try { await Clipboard.Default.SetTextAsync(payload); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dev] copy failed: {ex.Message}"); }
    }

    private void Reset()
    {
        _access = Access.Locked;
        CodeInput = string.Empty;
        AuthState = string.Empty;
        Log = string.Empty;
        DemoState = string.Empty;
        DemoBusy = false;
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(IsUnlocked));
        OnPropertyChanged(nameof(ShowDemo));
        OnPropertyChanged(nameof(ShowImport));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    public void ResetDraft()
    {
        IsPresented = false;
        Reset();
    }
}
