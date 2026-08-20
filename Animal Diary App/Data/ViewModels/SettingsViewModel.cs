namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Attribution;
using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.Services.Notifications;
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
    public ICommand OpenSubscribeCommand { get; }
    public ICommand OpenRedeemCommand { get; }
    public ICommand OpenDevCommand { get; }

    private readonly AppResetService _appResetService;
    private readonly SettingsService _settingsService;
    private readonly IAnalyticsService _analytics;
    private readonly ICloudAuthService _cloudAuth;
    private readonly ICloudSyncService _cloudSync;
    private readonly CloudSheetViewModel _cloudVM;
    private readonly SubscribeSheetViewModel _subscribeVM;
    private readonly RedeemCodeSheetViewModel _redeemVM;
    private readonly Animal_Diary_App.Data.Services.Billing.IEntitlementService _entitlements;
    private readonly DevSheetViewModel _devVM;

    /// <summary>Subtitle for the Settings → Subscription row: the current state, in the
    /// user's words (never "read-only" or "expired"). In the trial it carries the time
    /// left, e.g. "Free trial (3 days left)" or, near the end, "Free trial (12 minutes
    /// left)". Re-read when the panel opens and when the entitlement changes — not a
    /// ticking clock (a live countdown would push urgency this brand avoids).
    ///
    /// <para><b>Granted is listed explicitly, and the fallback arm is the reason.</b> It
    /// ends in the trial format, so any state that is not named here is described as a free
    /// trial with whatever time the (long-elapsed) trial clock has left. Adding a state to
    /// <c>AccessState</c> without a case here tells a granted user they are on a "Free trial
    /// (0 minutes left)". Name every state.</para></summary>
    public string SubscriptionRowSubtitle => _entitlements.State switch
    {
        Animal_Diary_App.Data.Services.Billing.AccessState.Subscribed
            => LocalizationManager.Instance.GetString("Settings_SubscriptionActive"),
        Animal_Diary_App.Data.Services.Billing.AccessState.Granted
            => LocalizationManager.Instance.Format(
                "Settings_SubscriptionGrantedFormat", FormatGrantEnd(_entitlements.GrantedUntilUtc)),
        Animal_Diary_App.Data.Services.Billing.AccessState.TrialExpired
            => LocalizationManager.Instance.GetString(
                // A lapsed year-long grant is not a lapsed 14-day trial, and TrialEverStarted
                // does not tell them apart (most granted owners did start a trial once).
                _entitlements.EverGranted ? "Settings_SubscriptionGrantEnded" : "Settings_SubscriptionEnded"),
        _ => LocalizationManager.Instance.Format(
            "Settings_SubscriptionTrialFormat", FormatTimeLeft(_entitlements.TrialTimeRemaining)),
    };

    /// <summary>Subtitle for the Settings → Redeem row. Names the end date once a code is
    /// running, and otherwise stays a plain invitation. Never a countdown.</summary>
    public string RedeemRowSubtitle =>
        _entitlements.GrantedUntilUtc is DateTime until && until > DateTime.UtcNow
            ? LocalizationManager.Instance.Format("Settings_RedeemActiveFormat", FormatGrantEnd(until))
            : LocalizationManager.Instance.GetString("Settings_RedeemSubtitle");

    /// <summary>A grant's end as a plain local date. A grant runs for months, so the time of
    /// day is noise; the date is the whole answer.</summary>
    private static string FormatGrantEnd(DateTime? untilUtc)
        => untilUtc is DateTime u ? u.ToLocalTime().ToString("d") : string.Empty;

    /// <summary>Human phrase for the time left: the largest sensible unit, pluralized.
    /// "3 days" / "1 day" / "5 hours" / "12 minutes".</summary>
    private static string FormatTimeLeft(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 1)
        {
            var d = (int)Math.Ceiling(remaining.TotalDays);
            return Plural(d, "Time_Day", "Time_Days");
        }
        if (remaining.TotalHours >= 1)
        {
            var h = (int)Math.Ceiling(remaining.TotalHours);
            return Plural(h, "Time_Hour", "Time_Hours");
        }
        var m = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return Plural(m, "Time_Minute", "Time_Minutes");
    }

    private static string Plural(int n, string singularKey, string pluralKey)
        => LocalizationManager.Instance.Format(n == 1 ? singularKey : pluralKey, n);
    private readonly DailyCareReminderScheduler _dailyReminders;
    private readonly MedicationReminderScheduler _reminders;
    private readonly IAdAttributionService _adAttribution;

    /// <summary>Whether to show the ad-measurement row at all. False on every platform and
    /// build without Meta attribution (iOS, Windows, macOS, or Meta switched off), where a
    /// switch would claim to control something that isn't running.</summary>
    public bool AdTrackingVisible => _adAttribution.IsAvailable;

    /// <summary>
    /// The owner's ad-measurement choice. On by default; this row is the way out.
    ///
    /// <para>Applied through the service rather than by writing the preference here,
    /// because switching off has to reach the SDK in this process too, not just persist a
    /// bool for the next launch.</para>
    /// </summary>
    public bool AdTrackingEnabled
    {
        get => _adAttribution.Enabled;
        set
        {
            if (value == _adAttribution.Enabled)
                return;
            _adAttribution.SetEnabled(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Whether a cloud account is signed in — pages pick the reset-confirm
    /// message with it (the cloud variant explains the ownership rule).</summary>
    public bool IsCloudSignedIn => _cloudAuth.IsSignedIn;

    public SettingsViewModel(AppResetService appResetService, SettingsService settingsService,
        IAnalyticsService analytics, ICloudAuthService cloudAuth, ICloudSyncService cloudSync,
        CloudSheetViewModel cloudVM, DevSheetViewModel devVM, DailyCareReminderScheduler dailyReminders,
        MedicationReminderScheduler reminders,
        SubscribeSheetViewModel subscribeVM, RedeemCodeSheetViewModel redeemVM,
        Animal_Diary_App.Data.Services.Billing.IEntitlementService entitlements,
        IAdAttributionService adAttribution)
    {
        _appResetService = appResetService;
        _settingsService = settingsService;
        _analytics = analytics;
        _cloudAuth = cloudAuth;
        _cloudSync = cloudSync;
        _cloudVM = cloudVM;
        _devVM = devVM;
        _dailyReminders = dailyReminders;
        _reminders = reminders;
        _subscribeVM = subscribeVM;
        _redeemVM = redeemVM;
        _entitlements = entitlements;
        _adAttribution = adAttribution;
        // Keep both rows fresh when the entitlement changes (purchase, expiry, redemption).
        _entitlements.StateChanged += () =>
        {
            OnPropertyChanged(nameof(SubscriptionRowSubtitle));
            OnPropertyChanged(nameof(RedeemRowSubtitle));
        };
        // The panel renders above the sheet, so opening the sheet closes the panel.
        OpenCloudCommand = new Command(() =>
        {
            IsPanelOpen = false;
            _cloudVM.OpenCommand.Execute(null);
        });
        // Subscription row → the subscribe sheet, tagged as opened from Settings.
        OpenSubscribeCommand = new Command(() =>
        {
            IsPanelOpen = false;
            _subscribeVM.Open(AnalyticsEvents.SubscribeSourceSettings);
        });
        // Redeem row → the access-code sheet. Settings is its only entry point.
        OpenRedeemCommand = new Command(() =>
        {
            IsPanelOpen = false;
            _redeemVM.Open();
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
            // The trial time left moves on; recompute the row subtitles for this opening.
            OnPropertyChanged(nameof(SubscriptionRowSubtitle));
            OnPropertyChanged(nameof(RedeemRowSubtitle));
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

    // ── Daily care reminder (device-local, app-wide) ─────────────────────────────
    // Off by default. Enabling arms today's reminder for each pet; changing the time
    // re-arms. State lives in DailyCareReminderSettings (Preferences); the scheduler
    // does the arm/cancel and skips paused pets and empty days.

    /// <summary>Master on/off for the once-a-day care reminder.</summary>
    public bool DailyReminderEnabled
    {
        get => DailyCareReminderSettings.Enabled;
        set
        {
            if (value == DailyCareReminderSettings.Enabled)
                return;
            DailyCareReminderSettings.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DailyReminderTimeVisible));
            EnableDailyReminderAsync(value).Forget();
        }
    }

    /// <summary>
    /// Turning the reminder on is an opt-in to notifications, so ask for the permission
    /// here — it was previously only ever requested when saving a medication, which
    /// meant a carer who enabled this without medications got a switch that said "on"
    /// and a reminder the OS would never deliver. A refusal doesn't undo the toggle:
    /// the setting is the carer's intent, and the Today page tells them delivery is
    /// blocked and offers the way back.
    /// </summary>
    private async Task EnableDailyReminderAsync(bool enabled)
    {
        try
        {
            if (enabled)
                await _reminders.RequestPermissionAsync();

            await _dailyReminders.RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] daily reminder toggle failed: {ex.Message}");
        }
    }

    /// <summary>The time of day the reminder fires. Bound to a native TimePicker.</summary>
    public TimeSpan DailyReminderTime
    {
        get => DailyCareReminderSettings.Time;
        set
        {
            if (value == DailyCareReminderSettings.Time)
                return;
            DailyCareReminderSettings.Time = value;
            OnPropertyChanged();
            _dailyReminders.RefreshAsync().Forget();
        }
    }

    /// <summary>The time row only makes sense once the reminder is on.</summary>
    public bool DailyReminderTimeVisible => DailyReminderEnabled;

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
            //                 pets (the ownership rule, docs/history/CLOUD_SYNC_PLAN.md §7).
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
