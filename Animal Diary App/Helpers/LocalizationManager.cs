namespace Animal_Diary_App.Helpers;

using System.ComponentModel;
using System.Globalization;
using System.Resources;

/// <summary>
/// Single source of truth for the app's current language. Holds a
/// <see cref="ResourceManager"/> over the <c>AppStrings</c> resources and exposes
/// strings through an indexer so XAML bindings (via <see cref="TranslateExtension"/>)
/// re-evaluate live when the culture changes.
///
/// Switching the language raises <see cref="PropertyChanged"/> with a null/empty
/// property name, which tells every active binding sourced from this manager to
/// refresh — so the whole UI re-translates without an app restart.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    private LocalizationManager()
    {
        // Base name = "{RootNamespace}.{folder path}.{file}" for the embedded
        // .resx (Resources/Strings/AppStrings.resx). Using ResourceManager
        // directly avoids depending on the generated designer class.
        _resourceManager = new ResourceManager(
            "Animal_Diary_App.Resources.Strings.AppStrings",
            typeof(LocalizationManager).Assembly);

        _currentCulture = CultureInfo.CurrentUICulture;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Two-letter code of the active language, e.g. "en" or "de".</summary>
    public string CurrentLanguage => _currentCulture.TwoLetterISOLanguageName;

    /// <summary>Indexer used by bindings: <c>{Binding [Key], Source=...}</c>.</summary>
    public string this[string key]
        => _resourceManager.GetString(key, _currentCulture) ?? key;

    /// <summary>Convenience accessor for use from C# code.</summary>
    public string GetString(string key)
        => _resourceManager.GetString(key, _currentCulture) ?? key;

    /// <summary>Localized <see cref="string.Format(string, object[])"/> helper.</summary>
    public string Format(string key, params object?[] args)
        => string.Format(_currentCulture, GetString(key), args);

    public void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // Null name => "all properties changed": every binding refreshes.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    /// <summary>Apply a two-letter language code ("en" / "de").</summary>
    public void SetLanguage(string twoLetterCode)
        => SetCulture(CultureInfo.GetCultureInfo(twoLetterCode));
}
