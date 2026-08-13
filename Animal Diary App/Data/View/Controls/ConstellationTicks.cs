namespace Animal_Diary_App.Data.View.Controls;

using System.Globalization;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The handful of dates floated under the sky.
///
/// <para>Time is the only axis that means anything here, so it is the only one that
/// gets labelled — and even it gets no rule, no ticks and no grid. The spacing is
/// chosen so the labels never crowd: as the owner zooms in, a year of month names
/// becomes weeks and then individual days, which is the same progressive reveal the
/// stars themselves do.</para>
///
/// <para>Lives beside the drawable rather than in the ViewModel because it is
/// lettering on a canvas, and it needs the canvas's width to decide how much of it
/// fits.</para>
/// </summary>
public static class ConstellationTicks
{
    /// <summary>Roughly the width of "24 Sept" plus air. Below this, labels touch.</summary>
    private const double MinimumSpacing = 82.0;

    /// <summary>Steps in days, coarsest last. A step is chosen, never computed, so the
    /// dates land on units a person recognises — a week, a month, a year — instead of
    /// on "every 43 days".</summary>
    private static readonly int[] Steps = { 1, 2, 7, 14, 30, 91, 182, 365 };

    public static List<SkyTick> Build(DateTime from, DateTime to, double contentWidth)
    {
        var ticks = new List<SkyTick>();
        var totalDays = (to - from).TotalDays;
        if (contentWidth <= 0 || totalDays <= 0)
            return ticks;

        var pixelsPerDay = contentWidth / totalDays;

        var step = Steps[^1];
        foreach (var candidate in Steps)
        {
            if (candidate * pixelsPerDay >= MinimumSpacing)
            {
                step = candidate;
                break;
            }
        }

        var monthly = step >= 28;
        var format = LocalizationManager.Instance.GetString(monthly ? "Sky_TickMonth" : "Sky_TickDay");
        var culture = CultureInfo.CurrentCulture;

        // Monthly and coarser walk real month boundaries: "1 Oct" beside "1 Nov" reads
        // as a calendar, where a fixed 30-day stride slowly slides off the months and
        // reads as nothing at all.
        if (monthly)
        {
            var months = Math.Max(1, (int)Math.Round(step / 30.4));
            var cursor = new DateTime(from.Year, from.Month, 1);
            if (cursor < from.Date)
                cursor = cursor.AddMonths(1);

            for (; cursor < to; cursor = cursor.AddMonths(months))
                ticks.Add(new SkyTick(
                    ConstellationLayout.XFor(cursor, from, to, contentWidth),
                    cursor.ToString(format, culture)));

            return ticks;
        }

        for (var cursor = from.Date; cursor < to; cursor = cursor.AddDays(step))
            ticks.Add(new SkyTick(
                ConstellationLayout.XFor(cursor, from, to, contentWidth),
                cursor.ToString(format, culture)));

        return ticks;
    }
}
