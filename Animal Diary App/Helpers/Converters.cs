using System;
using System.Globalization;
using Microsoft.Maui.Controls;
namespace Animal_Diary_App.Helpers;

/// <summary>First letter of a string, upper-cased — for the pet-chip avatar
/// initial (serif italic) in the rockpool Journal.</summary>
public class FirstLetterConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        var s = value?.ToString();
        return string.IsNullOrWhiteSpace(s)
            ? string.Empty
            : s.Trim()[..1].ToUpper(culture ?? CultureInfo.CurrentCulture);
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => value;
}

/// <summary>Maps a <see cref="MoodLevel"/> (0..5) to a bar height that encodes the
/// value accurately — this is real signal, so height must track the mood, not
/// decoration. None (no entry) renders as a short stub. Full scale is 30px.</summary>
public class MoodToHeightConverter : IValueConverter
{
    private const double Min = 6.0;
    private const double Max = 30.0;

    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is MoodLevel mood)
        {
            // Excellent(5) → 30, VeryBad(1) → ~10.8, None(0) → 6 (stub).
            int level = (int)mood;
            return Min + (Max - Min) * (level / 5.0);
        }
        return Min;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => value;
}

/// <summary>Localizes a stored pet-type key ("Dog", "Cat", …) for display via
/// <see cref="PetTypeNames.Localize"/>. Used as a <see cref="IMultiValueConverter"/>
/// so the second binding leg — sourced from <see cref="LocalizationManager"/> — makes
/// the text re-translate live when the language changes, just like a
/// <c>{loc:Translate}</c> span. The second value is only a refresh trigger and is
/// otherwise ignored.</summary>
public class PetTypeLocalizedConverter : IMultiValueConverter
{
    public object? Convert(object?[]? values, Type? targetType, object? parameter, CultureInfo? culture)
        => PetTypeNames.Localize(values is { Length: > 0 } ? values[0]?.ToString() : null);

    public object?[]? ConvertBack(object? value, Type?[]? targetTypes, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}

public class StringToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is string stringValue)
            return !string.IsNullOrEmpty(stringValue);
        return false;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        return value;
    }
}

public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is bool boolValue)
            return boolValue ? 1.0 : 0.5;
        return 1.0;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        return value;
    }
}

public class InvertedBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return true;
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

// (The pre-GraphicsView weight-chart converters — WeightToHeight/Max/Mid/Min —
// and the legacy placeholder converters were removed: no XAML referenced them.)