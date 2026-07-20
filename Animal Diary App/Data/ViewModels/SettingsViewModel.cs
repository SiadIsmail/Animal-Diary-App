namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Helpers;
using System.Windows.Input;

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
    /// Returns true if the user confirmed, false to cancel.
    /// </summary>
    public Func<Task<bool>>? ConfirmDeleteAllData { get; set; }

    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand DeleteAllDataCommand { get; }
    public ICommand SetLanguageCommand { get; }
    public ICommand OpenLinkCommand { get; }

    private readonly AppResetService _appResetService;
    private readonly SettingsService _settingsService;
    private readonly IAnalyticsService _analytics;

    public SettingsViewModel(AppResetService appResetService, SettingsService settingsService, IAnalyticsService analytics)
    {
        _appResetService = appResetService;
        _settingsService = settingsService;
        _analytics = analytics;
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
        if (ConfirmDeleteAllData != null)
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
