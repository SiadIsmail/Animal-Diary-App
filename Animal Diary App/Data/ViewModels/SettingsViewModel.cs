namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Helpers;
using System.Windows.Input;

/// <summary>What a signed-in "Delete all data" should destroy. Signed out there
/// is no choice — the device is all there is.</summary>
public enum ResetScope
{
    /// <summary>Wipe the device, keep the cloud backup (signs out).</summary>
    DeviceOnly,
    /// <summary>Wipe the device AND permanently delete owned pets from the
    /// cloud + leave shared pets (the ownership rule).</summary>
    Everything
}

public class SettingsViewModel : BaseViewModel
{
    private bool _isPanelOpen;
    public bool IsPanelOpen
    {
        get => _isPanelOpen;
        set => SetProperty(ref _isPanelOpen, value);
    }

    public event EventHandler? ResetCompleted;

    /// <summary>"Version 1.3.2 (7)" for the foot of the settings panel. Read from
    /// <see cref="AppInfo"/>, which surfaces the csproj's ApplicationDisplayVersion and
    /// ApplicationVersion — so a version bump needs no change here. Resolved per read
    /// so a live language switch re-translates it (this VM is a singleton).</summary>
    public string AppVersion => LocalizationManager.Instance.Format(
        "Settings_VersionFormat", AppInfo.Current.VersionString, AppInfo.Current.BuildString);

    /// <summary>
    /// Set by the active page to show a native confirmation dialog before deletion.
    /// Returns true if the user confirmed, false to cancel. Used when signed OUT.
    /// </summary>
    public Func<Task<bool>>? ConfirmDeleteAllData { get; set; }

    /// <summary>
    /// Set by the active page — the signed-in reset choice ("this device only —
    /// keep my backup" vs "everything, including my backup"). Null = cancelled.
    /// </summary>
    public Func<Task<ResetScope?>>? ConfirmDeleteAllDataCloud { get; set; }

    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand DeleteAllDataCommand { get; }
    public ICommand SetLanguageCommand { get; }
    public ICommand OpenLinkCommand { get; }
    public ICommand OpenCloudCommand { get; }
    public ICommand OpenDevCommand { get; }

    private readonly AppResetService _appResetService;
    private readonly SettingsService _settingsService;
    private readonly IAnalyticsService _analytics;
    private readonly ICloudAuthService _cloudAuth;
    private readonly ICloudSyncService _cloudSync;
    private readonly CloudSheetViewModel _cloudVM;
    private readonly DevSheetViewModel _devVM;

    /// <summary>Whether a cloud account is signed in — pages pick the reset-confirm
    /// message with it (the cloud variant explains the ownership rule).</summary>
    public bool IsCloudSignedIn => _cloudAuth.IsSignedIn;

    public SettingsViewModel(AppResetService appResetService, SettingsService settingsService,
        IAnalyticsService analytics, ICloudAuthService cloudAuth, ICloudSyncService cloudSync,
        CloudSheetViewModel cloudVM, DevSheetViewModel devVM)
    {
        _appResetService = appResetService;
        _settingsService = settingsService;
        _analytics = analytics;
        _cloudAuth = cloudAuth;
        _cloudSync = cloudSync;
        _cloudVM = cloudVM;
        _devVM = devVM;
        // The panel renders above the sheet, so opening the sheet closes the panel.
        OpenCloudCommand = new Command(() =>
        {
            IsPanelOpen = false;
            _cloudVM.OpenCommand.Execute(null);
        });
        // Hidden developer panel (gated by a code inside the sheet).
        OpenDevCommand = new Command(() =>
        {
            IsPanelOpen = false;
            _devVM.OpenCommand.Execute(null);
        });
        OpenSettingsCommand = new Command(() =>
        {
            IsPanelOpen = true;
            _analytics.Track(AnalyticsEvents.SettingsOpened);
        });
        CloseSettingsCommand = new Command(() => IsPanelOpen = false);
        DeleteAllDataCommand = new Command(async () => await OnDeleteAllDataAsync());
        SetLanguageCommand = new Command<string>(async code => await SetLanguageAsync(code));
        OpenLinkCommand = new Command<string>(async url => await OpenLinkAsync(url));
    }

    private static async Task OpenLinkAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            await Launcher.OpenAsync(uri);
        }
        catch
        {
            // Ignore — no browser available or the launch was cancelled.
        }
    }

    /// <summary>Two-letter code of the active language ("en" / "de").</summary>
    public string CurrentLanguage => LocalizationManager.Instance.CurrentLanguage;

    // Convenience flags for highlighting the selected language chip in the panel.
    public bool IsGerman => CurrentLanguage == "de";
    public bool IsEnglish => CurrentLanguage != "de";

    private async Task SetLanguageAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode) || languageCode == CurrentLanguage)
            return;

        LocalizationManager.Instance.SetLanguage(languageCode);
        await _settingsService.SetLanguageAsync(languageCode);

        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(IsGerman));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(AppVersion));
    }

    private async Task OnDeleteAllDataAsync()
    {
        if (_cloudAuth.IsSignedIn)
        {
            // Signed in, the reset is a CHOICE (the owner was surprised when the
            // backup vanished — the two consequences must be picked explicitly):
            //   DeviceOnly  → wipe device, keep backup, sign out.
            //   Everything  → also tombstone owned pets cloud-wide + leave shared
            //                 pets (the ownership rule, CLOUD_SYNC_PLAN.md §7).
            var scope = ConfirmDeleteAllDataCloud != null
                ? await ConfirmDeleteAllDataCloud()
                : ResetScope.Everything;
            if (scope == null)
                return;

            if (scope == ResetScope.Everything)
            {
                // Best-effort: an offline reset still wipes the device — the one
                // gap is that the cloud copy survives until the next sign-in.
                try
                {
                    await _cloudSync.DeleteCloudDataAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Cloud] reset cloud-delete failed: {ex.Message}");
                }
            }

            try { await _cloudAuth.SignOutAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Cloud] reset sign-out failed: {ex.Message}"); }
            await _cloudSync.DisableBackupAsync();
        }
        else if (ConfirmDeleteAllData != null)
        {
            var confirmed = await ConfirmDeleteAllData();
            if (!confirmed)
                return;
        }

        await _appResetService.ResetDataAsync();
        IsPanelOpen = false;
        ResetCompleted?.Invoke(this, EventArgs.Empty);
    }
}
