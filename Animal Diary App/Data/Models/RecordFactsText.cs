namespace Animal_Diary_App.Data.Models;

using System.Globalization;
using Animal_Diary_App.Helpers;

/// <summary>
/// How a <see cref="RecordFacts"/> reads. One renderer for every surface that shows
/// one — Today's card sheet, the Constellation legend, and the appointment summary —
/// for the same reason the arithmetic is one service: a line that appears differently
/// on one surface is a line the app chose to phrase differently for one record.
///
/// <para>Every method resolves its strings per call and caches nothing, so a live
/// language switch reaches an open sheet (AI/coding-standards.md).</para>
///
/// <para><b>Nothing here may introduce a word of direction or quality.</b> No up, down,
/// higher, lower, better, worse, improving, stable, high, low or normal — see the
/// doctrine on <see cref="RecordFacts"/>. The vocabulary is counts, dates and the
/// owner's own numbers.</para>
/// </summary>
public static class RecordFactsText
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private const string Separator = " · ";

    /// <summary>"84 things written down", matching the wording the sky already uses for
    /// a count of entries. Doses get their own noun because a dose nobody answered is
    /// not something the owner wrote down.</summary>
    public static string Count(RecordFacts facts)
    {
        if (facts.Doses is not null)
            return facts.Count == 1
                ? Loc.GetString("Facts_DoseOne")
                : Loc.Format("Facts_DoseMany", facts.Count);

        return Things(facts.Count);
    }

    /// <summary>The same sentence from a bare count — for the Constellation legend,
    /// which already holds the moments in memory and has no snapshot to build.</summary>
    public static string Things(int count) =>
        count == 1 ? Loc.GetString("Facts_CountOne") : Loc.Format("Facts_CountMany", count);

    /// <summary>"Last 90 days" — the stretch, stated plainly beside the count.</summary>
    public static string Range(RecordFacts facts) =>
        Loc.Format("Facts_LastDays", Math.Max(1, (facts.To.Date - facts.From.Date).Days + 1));

    /// <summary>The count and the stretch on one line.</summary>
    public static string CountAndRange(RecordFacts facts) =>
        Count(facts) + Separator + Range(facts);

    /// <summary>
    /// "Night 4 · Morning 61 · Afternoon 12 · Evening 7".
    ///
    /// <para><b>All four, always, in fixed order</b> — zeros included. Dropping an empty
    /// band or leading with the busiest one would make this the app naming the finding
    /// instead of the owner seeing it.</para>
    /// </summary>
    public static string DayParts(RecordFacts facts) => DayParts(facts.DayParts);

    /// <inheritdoc cref="DayParts(RecordFacts)"/>
    public static string DayParts(DayPartCounts bands)
    {
        var counts = bands.InOrder;
        var parts = new string[counts.Count];
        for (var i = 0; i < counts.Count; i++)
            parts[i] = $"{Loc.GetString(DayPartCounts.LabelKey((DayPart)i))} {counts[i]}";
        return string.Join(Separator, parts);
    }

    /// <summary>
    /// "Lowest 3.1 · Highest 22.4 · Latest 14.2 on 19 Aug", or empty for a record that
    /// carries no numbers. Three recorded values and a date — never a mean, a spread, or
    /// which way they went.
    /// </summary>
    public static string Values(RecordFacts facts)
    {
        if (!facts.HasValues)
            return string.Empty;

        var parts = new List<string>(3)
        {
            Loc.Format("Facts_Lowest", Number(facts.Lowest!.Value)),
            Loc.Format("Facts_Highest", Number(facts.Highest!.Value)),
        };

        if (facts.Latest is decimal latest && facts.LatestOn is DateTime on)
            parts.Add(Loc.Format("Facts_Latest", Number(latest), Day(on)));

        return string.Join(Separator, parts);
    }

    /// <summary>"308 given · 11 skipped · 6 not recorded", or empty for anything that
    /// is not medication. Three raw counts: no percentage, no adherence score, and the
    /// word is <b>not recorded</b>, never "missed" (AI/app-voice.md §9).</summary>
    public static string Doses(RecordFacts facts)
    {
        if (facts.Doses is not DoseCounts d)
            return string.Empty;

        return string.Join(Separator,
            Loc.Format("Facts_Given", d.Given),
            Loc.Format("Facts_Skipped", d.Skipped),
            Loc.Format("Facts_NotRecorded", d.NotRecorded));
    }

    /// <summary>Nothing in the range. States the absence and stops — an empty stretch is
    /// a stretch where nothing happened, never a failure to engage (AI/app-voice.md §11).</summary>
    public static string Nothing(RecordFacts facts) =>
        Loc.Format("Facts_Nothing", Math.Max(1, (facts.To.Date - facts.From.Date).Days + 1));

    /// <summary>One generic number format across kg, mmol/L, mL, grams and whatever unit
    /// an owner typed. Current culture, so a German reader sees 22,4.</summary>
    private static string Number(decimal value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>"19 Aug" — the same short form Today's cards use.</summary>
    private static string Day(DateTime date) =>
        date.ToString("d MMM", CultureInfo.CurrentCulture);
}
