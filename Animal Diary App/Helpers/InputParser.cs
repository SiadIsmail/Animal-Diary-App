namespace Animal_Diary_App.Data.Helpers;
using System.Globalization;

public static class InputParser
{
    public static bool TryParsePositive(string input, out decimal value)
    {
        if (!decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        if (value <= 0)
        {
            return false;
        }

        return true;
    }
}