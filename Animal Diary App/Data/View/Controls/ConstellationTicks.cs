namespace Animal_Diary_App.Data.View.Controls;

using System.Globalization;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The lettering on the sky: the hours down the left gutter, the dates along the
/// foot, and the quarters around the ring.
///
/// <para>Both axes are time and <b>both are labelled</b>. An earlier version labelled
/// only the dates, because the other axis meant nothing — which is exactly why the
/// picture was unreadable. A label is not clutter when it is the difference between a
/// coordinate and a decoration.</para>
///
/// <para>Lives beside the drawable rather than in the ViewModel because it is
/// lettering on a canvas, and it needs the canvas's size to decide how much fits.</para>
/// </summary>
public static class ConstellationTicks
{
    /// <summary>Roughly the width of "24 Sept" plus air. Below this, dates touch.</summary>
    private const double MinimumSpacing = 82.0;

    /// <summary>Steps in days, coarsest last. A step is chosen, never computed, so the
    /// dates land on units a person recognises — a week, a month, a year — instead of
    /// on "every 43 days".</summary>
    private static readonly int[] Steps = { 1, 2, 7, 14, 30, 91, 182, 365 };

    /// <summary>The quarters of a day, midnight first.</summary>
    private static readonly int[] QuarterHours = { 0, 6, 12, 18 };

    /// <summary>
    /// The History lens: hours pinned down the gutter, dates along the foot.
    /// </summary>
    /// <param name="worldWidth">The plot's width at zoom 1. Date positions come back in
    /// these units — the camera is applied when they are drawn.</param>
    /// <param name="zoom">Only chooses how MANY dates fit: the step is picked against
    /// the zoomed width, so zooming in turns month names into weeks and then into
    /// individual days. The positions stay in world units, or the dates would drift
    /// against the entries they label.</param>
    public static List<SkyTick> Grid(DateTime from, DateTime to, double worldWidth, double height, double zoom = 1)
    {
        var ticks = new List<SkyTick>();
        var totalDays = (to - from).TotalDays;
        if (worldWidth <= 0 || height <= 0 || totalDays <= 0)
            return ticks;

        // ── Hours, in the gutter, at the height they actually mean ──
        var loc = LocalizationManager.Instance;
        foreach (var hour in QuarterHours)
        {
            ticks.Add(new SkyTick(
                ConstellationLayout.HourGutter / 2,
                ConstellationLayout.HourY(hour / 24.0, height) + 3,
                loc.Format("Sky_DialHour", hour),
                Pinned: true));
        }

        // ── Dates, along the foot ──
        var y = height - 5;
        var pixelsPerDay = worldWidth * (zoom <= 0 ? 1 : zoom) / totalDays;

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
        var format = loc.GetString(monthly ? "Sky_TickMonth" : "Sky_TickDay");
        var culture = CultureInfo.CurrentCulture;

        // Monthly and coarser walk real month boundaries: "1 Oct" beside "1 Nov" reads
        // as a calendar, where a fixed 30-day stride slides off the months.
        if (monthly)
        {
            var months = Math.Max(1, (int)Math.Round(step / 30.4));
            var cursor = new DateTime(from.Year, from.Month, 1);
            if (cursor < from.Date)
                cursor = cursor.AddMonths(1);

            for (; cursor < to; cursor = cursor.AddMonths(months))
                ticks.Add(new SkyTick(
                    ConstellationLayout.XFor(cursor, from, to, worldWidth), y,
                    cursor.ToString(format, culture)));

            return ticks;
        }

        for (var cursor = from.Date; cursor < to; cursor = cursor.AddDays(step))
            ticks.Add(new SkyTick(
                ConstellationLayout.XFor(cursor, from, to, worldWidth), y,
                cursor.ToString(format, culture)));

        return ticks;
    }

    /// <summary>
    /// The ring's four quarter marks: hours at a one-day fold, day numbers beyond it.
    ///
    /// <para>Four and no more. A ring of twenty-four numbers is instrumentation, and
    /// this is a sky — the quarters are enough to read a wedge by, and enough to
    /// describe one out loud to a vet.</para>
    /// </summary>
    public static List<SkyTick> Ring(double periodDays, double width, double height)
    {
        var ticks = new List<SkyTick>();
        if (width <= 0 || height <= 0 || periodDays <= 0)
            return ticks;

        var outer = ConstellationLayout.RingOuter(width, height);
        if (outer <= 0)
            return ticks;

        var centreX = width / 2;
        var centreY = height / 2;
        var radius = outer + 16;

        var loc = LocalizationManager.Instance;
        var wholeDay = periodDays <= 1.0001;

        for (int q = 0; q < 4; q++)
        {
            var angle = q / 4.0 * Math.Tau - Math.PI / 2;
            var label = wholeDay
                ? loc.Format("Sky_DialHour", QuarterHours[q])
                : loc.Format("Sky_FoldDay", (int)Math.Round(periodDays * q / 4) + 1);

            ticks.Add(new SkyTick(
                centreX + Math.Cos(angle) * radius,
                centreY + Math.Sin(angle) * radius + 4,
                label));
        }

        return ticks;
    }
}
