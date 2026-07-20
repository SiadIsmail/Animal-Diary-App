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

        // Felova selection accent: teal tint + teal stroke when selected,
        // plain glass + hairline otherwise.
        var selBg = Color.FromArgb("#21149081");    // teal-tint
        var selStroke = Color.FromArgb("#149081");  // teal
        var idleBg = Color.FromArgb("#99FFFFFF");   // glass
        var idleStroke = Color.FromArgb("#D9FFFFFF"); // glass-line

        GermanOption.BackgroundColor = germanSelected ? selBg : idleBg;
        GermanOption.Stroke = germanSelected ? selStroke : idleStroke;

        EnglishOption.BackgroundColor = !germanSelected ? selBg : idleBg;
        EnglishOption.Stroke = !germanSelected ? selStroke : idleStroke;
    }

    private async void OnContinueTapped(object? sender, TappedEventArgs e)
    {
        LocalizationManager.Instance.SetLanguage(_selectedLanguage);
        await _settingsService.SetLanguageAsync(_selectedLanguage);
        _onContinue(_selectedLanguage);
    }
}
