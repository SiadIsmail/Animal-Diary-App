namespace Animal_Diary_App.Data.Models;

using System.Globalization;
using Animal_Diary_App.Helpers;

/// <summary>
/// How a <see cref="RecordFacts"/> reads. One renderer for every surface that shows
/// one: Today's card sheet, the Constellation legend, and the appointment summary,
/// for the same reason the arithmetic is one service: a line that appears differently
/// on one surface is a line the app chose to phrase differently for one record.
///
/// <para>Every method resolves its strings per call and caches nothing, so a live
/// language switch reaches an open sheet (AI/coding-standards.md).</para>
///
/// <para><b>Nothing here may introduce a word of direction or quality.</b> No up, down,
/// higher, lower, better, worse, improving, stable, high, low or normal: see the
/// doctrine on <see cref="RecordFacts"/>. The vocabulary is counts, dates and the
/// owner's own numbers.</para>
/// </summary>
public static class RecordFactsText
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    private const string Separator = " · ";

    /// <summary>
    /// "51 readings", "4 weigh-ins", "18 days recorded", "13 seizures", "308 doses".
    ///
    /// <para><b>The noun is the record's own.</b> This used to be "N things written down"
    /// for everything, which meant the phrase appeared four times down one scroll of
    /// Today: once it is warm, four times it is a template, and said less than the
    /// specific word would. "18 things written down" for mood is genuinely ambiguous: 18
    /// entries or 18 days? (It is days. Nothing said so.)</para>
    ///
    /// <para>Water and appetite keep the generic "N entries": each of them spans two
    /// stores (millilitres and a relative word), and there is no one noun that is true of
    /// both. A record with no honest word of its own gets the plain one rather than an
    /// invented one.</para>
    ///
    /// <para>An owner-defined tracker is counted against ITS OWN NAME, verbatim and
    /// unpluralized: "12 × Walk". Their word is the right noun and it is never
    /// translated; inflecting user text for plural is exactly what AI/app-voice.md §20
    /// rules out.</para>
    /// </summary>
    /// <param name="recordName">The custom tracker's own name. Ignored for every shipped
    /// record, and optional so a caller that does not have it still gets a true line.</param>
    public static string Count(RecordFacts facts, string recordName = "")
    {
        if (facts.Doses is not null)
            return facts.Count == 1
                ? Loc.GetString("Facts_DoseOne")
                : Loc.Format("Facts_DoseMany", facts.Count);

        if (facts.Kind.IsCustom)
            return recordName.Length > 0
                ? Loc.Format("Facts_CountCustom", facts.Count, recordName)
                : Plural(facts.Count, "Facts_CountEntryOne", "Facts_CountEntryMany");

        return facts.Kind.BuiltIn switch
        {
            TodayCardId.Weight => Plural(facts.Count, "Facts_CountWeighInOne", "Facts_CountWeighInMany"),
            TodayCardId.Mood => Plural(facts.Count, "Facts_CountDayOne", "Facts_CountDayMany"),
            TodayCardId.Glucose => Plural(facts.Count, "Facts_CountReadingOne", "Facts_CountReadingMany"),
            TodayCardId.Seizure => Plural(facts.Count, "Facts_CountSeizureOne", "Facts_CountSeizureMany"),
            _ => Plural(facts.Count, "Facts_CountEntryOne", "Facts_CountEntryMany"),
        };
    }

    /// <summary>Singular and plural as two whole strings, never an inflected fragment,
    /// German does not follow English here (AI/app-voice.md §20).</summary>
    private static string Plural(int count, string oneKey, string manyKey) =>
        count == 1 ? Loc.GetString(oneKey) : Loc.Format(manyKey, count);

    /// <summary>"84 things written down": the Constellation legend, and ONLY it. That
    /// surface counts a genuinely mixed set of records, where no better noun exists;
    /// everywhere else states the record's own word (see <see cref="Count"/>).</summary>
    public static string Things(int count) =>
        count == 1 ? Loc.GetString("Facts_CountOne") : Loc.Format("Facts_CountMany", count);

    /// <summary>"Last 90 days": the stretch, stated plainly beside the count.</summary>
    public static string Range(RecordFacts facts) =>
        Loc.Format("Facts_LastDays", Math.Max(1, (facts.To.Date - facts.From.Date).Days + 1));

    /// <summary>The count and the stretch on one line.</summary>
    public static string CountAndRange(RecordFacts facts, string recordName = "") =>
        Count(facts, recordName) + Separator + Range(facts);

    /// <summary>
    /// "Night 4 · Morning 61 · Afternoon 12 · Evening 7", or <b>empty</b> for a record
    /// whose entry times are a fact about the owner's routine rather than about the pet.
    ///
    /// <para><b>All four, always, in fixed order</b>: zeros included. Dropping an empty
    /// band or leading with the busiest one would make this the app naming the finding
    /// instead of the owner seeing it. Which RECORDS state the row at all is decided in
    /// the builder by the shape of the store; see Data/Models/RecordFacts.cs.</para>
    /// </summary>
    public static string DayParts(RecordFacts facts) =>
        facts.StatesDayParts ? DayParts(facts.DayParts) : string.Empty;

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
    /// carries no numbers. Three recorded values and a date, never a mean, a spread, or
    /// which way they went.
    /// </summary>
    /// <param name="includeLatest">
    /// False where something else on the same screen already states the last reading.
    /// Today's stat cards ARE "the last reading and when", and the owner put them there
    /// by hand; repeating it eight hundred pixels down under the chart is two features
    /// stating one fact. The cards own "latest", the charts own "what's been happening",
    /// and <b>Lowest and Highest stay on every record either way</b>, no card states
    /// those.
    /// </param>
    public static string Values(RecordFacts facts, bool includeLatest = true)
    {
        if (!facts.HasValues)
            return string.Empty;

        var parts = new List<string>(3)
        {
            Loc.Format("Facts_Lowest", Number(facts, facts.Lowest!.Value)),
            Loc.Format("Facts_Highest", Number(facts, facts.Highest!.Value)),
        };

        if (includeLatest && facts.Latest is decimal latest && facts.LatestOn is DateTime on)
            parts.Add(Loc.Format("Facts_Latest", Number(facts, latest), Day(on)));

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

    /// <summary>Nothing in the range. States the absence and stops: an empty stretch is
    /// a stretch where nothing happened, never a failure to engage (AI/app-voice.md §11).</summary>
    public static string Nothing(RecordFacts facts) =>
        Loc.Format("Facts_Nothing", Math.Max(1, (facts.To.Date - facts.From.Date).Days + 1));

    /// <summary>One generic number format across mmol/L, mL, grams and whatever unit an
    /// owner typed. Current culture, so a German reader sees 22,4.
    ///
    /// <para>Weight is the exception and routes to <see cref="WeightText"/>: it is the one
    /// reading rendered on four other surfaces, and it has to read identically on all of
    /// them. That is a rule about weight, not about this panel, so it lives there.</para>
    /// </summary>
    private static string Number(RecordFacts facts, decimal value) =>
        facts.Kind.Is(TodayCardId.Weight)
            ? WeightText.Number(value)
            : value.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>"19 Aug": the same short form Today's cards use.</summary>
    private static string Day(DateTime date) =>
        date.ToString("d MMM", CultureInfo.CurrentCulture);
}
