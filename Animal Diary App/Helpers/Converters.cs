using System;
using System.Globalization;
using Microsoft.Maui.Controls;
namespace Animal_Diary_App.Helpers;

public class ZeroToWeightPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && intValue == 0)
            return "Make a weight entry for today!";

        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}

public class EmptyToMoodPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string stringValue && string.IsNullOrEmpty(stringValue))
            return "Make a mood entry for today!";

        return value?.ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}