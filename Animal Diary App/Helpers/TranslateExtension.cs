namespace Animal_Diary_App.Helpers;

using System;
using Microsoft.Maui.Controls.Xaml;

/// <summary>
/// XAML markup extension that binds a UI property to a localized string.
///
/// Usage (after declaring the namespace
/// <c>xmlns:loc="clr-namespace:Animal_Diary_App.Helpers"</c>):
/// <code>Text="{loc:Translate Welcome_Tagline}"</code>
///
/// It returns a <see cref="Binding"/> against the <see cref="LocalizationManager"/>
/// singleton's string indexer. Because the manager raises a null-named
/// PropertyChanged when the language changes, the binding re-reads the new
/// translation instantly — giving live language switching with no restart.
/// </summary>
// ProvideValue ignores the service provider entirely (it only ever returns a
// Binding against the LocalizationManager singleton), so XamlC is told not to
// build one per usage — otherwise every {loc:Translate} site warns XC0103.
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    /// <summary>Resource key to look up (e.g. "Settings_Title").</summary>
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider)
        => new Binding
        {
            Mode = BindingMode.OneWay,
            Path = $"[{Key}]",
            Source = LocalizationManager.Instance,
        };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
        => ProvideValue(serviceProvider);
}
