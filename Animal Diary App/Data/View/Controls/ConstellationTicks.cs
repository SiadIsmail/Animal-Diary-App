namespace Animal_Diary_App.Data.View.Controls;

using System.Globalization;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;

/// <summary>
/// The handful of labels floated on the sky — dates along the timeline, hours around
/// the dial, days across a fold.
///
/// <para>Time is the only axis that means anything here, so it is the only one that
/// gets labelled — and even it gets no rule, no ticks and no grid. On the Timeline the
/// spacing adapts to the zoom: a year of month names becomes weeks and then individual
/// days, which is the same progressive reveal the stars themselves do.</para>
///
/// <para>Lives beside the drawable rather than in the ViewModel because it is
/// lettering on a canvas, and it needs the canvas's size to decide how much fits.</para>
/// </summary>
public static class ConstellationTicks
{
    /// <summary>Roughly the width of "24 Sept" plus air. Below this, labels touch.</summary>
    private const double MinimumSpacing = 82.0;

    /// <summary>How far above the foot of the card the timeline's dates sit.</summary>
    private const float FootOffset = 7f;

    /// <summary>Steps in days, coarsest last. A step is chosen, never computed, so the
    /// dates land on units a person recognises — a week, a month, a year — instead of
    /// on "every 43 days".</summary>
    private static readonly int[] Steps = { 1, 2, 7, 14, 30, 91, 182, 365 };

    /// <summary>The four quarters of the dial, midnight at the top and clockwise.</summary>
    private static readonly int[] DialHours = { 0, 6, 12, 18 };

    /// <param name="worldWidth">The stretch's width at zoom 1. Positions come back in
    /// these units — the camera is applied when they are drawn.</param>
    /// <param name="height">Canvas height; the dates sit at its foot.</param>
    /// <param name="zoom">Only chooses how MANY dates fit: the step is picked against
    /// the zoomed width, so zooming in turns month names into weeks and then into
    /// individual days. The positions themselves must stay in world units, or the
    /// dates would drift against the stars they label.</param>
    public static List<SkyTick> Build(DateTime from, DateTime to, double worldWidth, double height, double zoom = 1)
    {
        var ticks = new List<SkyTick>();
        var totalDays = (to - from).TotalDays;
        if (worldWidth <= 0 || totalDays <= 0)
            return ticks;

        var y = height - FootOffset;
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
                    ConstellationLayout.XFor(cursor, from, to, worldWidth),
                    y,
                    cursor.ToString(format, culture)));

            return ticks;
        }

        for (var cursor = from.Date; cursor < to; cursor = cursor.AddDays(step))
            ticks.Add(new SkyTick(
                ConstellationLayout.XFor(cursor, from, to, worldWidth),
                y,
                cursor.ToString(format, culture)));

        return ticks;
    }

    /// <summary>
    /// The ring's four quarter marks.
    ///
    /// <para>At a one-day fold they are hours and the thing is a clock face; beyond
    /// that they are day numbers. Four and no more either way: a ring of twenty-four
    /// numbers is instrumentation, and this is a sky — the quarters are enough to read
    /// a wedge by, and enough to describe one out loud to a vet.</para>
    /// </summary>
    public static List<SkyTick> Ring(double periodDays, double width, double height)
    {
        var ticks = new List<SkyTick>();
        if (width <= 0 || height <= 0 || periodDays <= 0)
            return ticks;

        var centreX = width / 2;
        var centreY = height / 2;
        var radius = Math.Min(width, height) / 2 - 10;
        if (radius <= 0)
            return ticks;

        var loc = LocalizationManager.Instance;
        var wholeDay = periodDays <= 1.0001;

        for (int q = 0; q < 4; q++)
        {
            var angle = q / 4.0 * Math.Tau - Math.PI / 2;
            var label = wholeDay
                ? loc.Format("Sky_DialHour", q * 6)
                : loc.Format("Sky_FoldDay", (int)Math.Round(periodDays * q / 4) + 1);

            ticks.Add(new SkyTick(
                centreX + Math.Cos(angle) * radius,
                centreY + Math.Sin(angle) * radius + 4,
                label));
        }

        return ticks;
    }

    /// <summary>
    /// The wall's two sets of labels: the hours pinned across the top, and a date every
    /// so often down the left, riding with its row.
    ///
    /// <para>The dates are spaced so they never crowd, which on a year of eight-pixel
    /// rows means one a fortnight. They exist so a row can be named out loud — "it was
    /// the Tuesday" — not so the wall can be measured off.</para>
    /// </summary>
    public static List<SkyTick> Wall(DateTime from, int dayCount, double rowHeight, double width, double height)
    {
        var ticks = new List<SkyTick>();
        if (width <= 0 || height <= 0 || rowHeight <= 0 || dayCount <= 0)
            return ticks;

        var inset = ConstellationLayout.WallInset;
        var plotWidth = width - inset - 8;
        if (plotWidth <= 0)
            return ticks;

        // Hours across the top, pinned to the card so they stay readable while the
        // nights scroll under them.
        foreach (var hour in DialHours)
        {
            ticks.Add(new SkyTick(
                inset + hour / 24.0 * plotWidth,
                11,
                LocalizationManager.Instance.Format("Sky_DialHour", hour),
                Pinned: true));
        }

        // One date per N rows — whatever N keeps them from touching.
        var every = Math.Max(1, (int)Math.Ceiling(18 / rowHeight));
        var format = LocalizationManager.Instance.GetString("Sky_TickDay");
        var culture = CultureInfo.CurrentCulture;

        for (int row = 0; row < dayCount; row += every)
        {
            ticks.Add(new SkyTick(
                inset / 2,
                (row + 0.5) * rowHeight + 3,
                from.Date.AddDays(row).ToString(format, culture)));
        }

        return ticks;
    }

}
