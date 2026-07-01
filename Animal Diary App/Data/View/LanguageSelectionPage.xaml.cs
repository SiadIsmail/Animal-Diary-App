namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;

public partial class LanguageSelectionPage : ContentPage
{
    private readonly SettingsService _settingsService;
    private readonly Action<string> _onContinue;
    private string _selectedLanguage;

    public LanguageSelectionPage(SettingsService settingsService, Action<string> onContinue)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _onContinue = onContinue;

        // Pre-select whatever language the UI is currently showing (seeded from
        // the device locale by App.StartAsync).
        _selectedLanguage = LocalizationManager.Instance.CurrentLanguage == "de" ? "de" : "en";
        UpdateSelectionVisuals();
    }

    private void OnGermanTapped(object? sender, TappedEventArgs e) => Select("de");

    private void OnEnglishTapped(object? sender, TappedEventArgs e) => Select("en");

    private void Select(string languageCode)
    {
        _selectedLanguage = languageCode;
        // Apply immediately so the page (title, subtitle, Continue) re-translates.
        LocalizationManager.Instance.SetLanguage(languageCode);
        UpdateSelectionVisuals();
    }

    private void UpdateSelectionVisuals()
    {
        var germanSelected = _selectedLanguage == "de";

        GermanOption.BackgroundColor = germanSelected ? Color.FromArgb("#809B6FCC") : Color.FromArgb("#40FFFFFF");
        GermanOption.Stroke = germanSelected ? Color.FromArgb("#B39B6FCC") : Color.FromArgb("#60FFFFFF");

        EnglishOption.BackgroundColor = !germanSelected ? Color.FromArgb("#809B6FCC") : Color.FromArgb("#40FFFFFF");
        EnglishOption.Stroke = !germanSelected ? Color.FromArgb("#B39B6FCC") : Color.FromArgb("#60FFFFFF");
    }

    private async void OnContinueTapped(object? sender, TappedEventArgs e)
    {
        LocalizationManager.Instance.SetLanguage(_selectedLanguage);
        await _settingsService.SetLanguageAsync(_selectedLanguage);
        _onContinue(_selectedLanguage);
    }
}
