namespace Animal_Diary_App.Helpers;

using System.Globalization;

/// <summary>
/// How a weight reads. <b>One formatter, every surface</b>: Today's meta line, the stat
/// cards, the Journal timeline, the Constellation, the facts panel and the vet report.
///
/// <para><b>Why this exists.</b> Today's meta line rendered <c>5.19 kg</c> while the stat
/// card beside it rendered <c>5.2 kg</c>: the same weigh-in, two numbers, one screen. In
/// an app people trust with medical numbers that is a trust bug wearing a cosmetic
/// disguise: a reader who notices it has to work out which of the two is the number they
/// typed, and the answer was "neither, exactly".</para>
///
/// <para><b>Two decimals, trailing zeros dropped</b> (<c>0.##</c>, the same shape the
/// report's trend values already used). It preserves what the owner typed rather than
/// rounding it: 5.19 stays 5.19. Truncating someone's own reading is the one thing a
/// record may not do to it, and a card is not so narrow that it needs to.</para>
///
/// <para>Current culture, so a German reader sees <c>5,19</c>.</para>
/// </summary>
public static class WeightText
{
    /// <summary>The format every weight in the app is written in. Public so a surface
    /// that must pass a format string (a MigraDoc cell, a XAML StringFormat) uses the
    /// same one rather than a copy of it.</summary>
    public const string Format = "0.##";

    /// <summary>The number alone: <c>"5.19"</c>.</summary>
    public static string Number(decimal kg) =>
        kg.ToString(Format, CultureInfo.CurrentCulture);

    /// <summary>The unit alone, with its own leading space: <c>" kg"</c>. Exposed so a
    /// surface that styles the number and the unit separately (Today's stat card sets
    /// them in two sizes) still gets it from here rather than naming the resource key
    /// itself. Resolved per call and cached nowhere: a live language switch has to reach
    /// an open page.</summary>
    public static string Unit => LocalizationManager.Instance.GetString("Common_KgSuffix");

    /// <summary>The number with the unit: <c>"5.19 kg"</c>.</summary>
    public static string WithUnit(decimal kg) => Number(kg) + Unit;
}
