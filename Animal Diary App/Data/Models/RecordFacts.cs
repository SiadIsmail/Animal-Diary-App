namespace Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Facts about the record.
//
//  Felova states facts about what the OWNER WROTE DOWN. It never makes a claim
//  about the animal. Everything in this file is arithmetic on the diary: counts,
//  the lowest and highest number in a range, the first and last date: computed
//  IDENTICALLY for every kind, from the owner's own entries.
//
//  What is deliberately absent, and must stay absent:
//
//   • NO AVERAGE. A mean of readings taken at irregular times is a number that
//     looks meaningful and is not. It is the closest thing to a claim this whole
//     feature could contain.
//   • No trend, delta, direction or comparison. Nothing here is up, down, higher,
//     lower, better, worse, improving, stable, high, low or normal.
//   • No ranking, no "the interesting one". The day-part counts are FOUR FIXED
//     BANDS rendered in a fixed order, always, never a computed "peak window".
//     A window chosen because it holds the most entries is the app selecting the
//     finding; four fixed numbers let the owner see it themselves, and that
//     distinction is the entire feature.
//
//  WHERE THE DAY-PART ROW APPEARS, AND WHERE IT DOES NOT.
//
//  Day-parts are stated where the entry's TIME IS A FACT ABOUT THE PET, and
//  suppressed where it is a fact about the owner's routine. That line is already in
//  the data model: for an EVENT store the moment is the datum: a 7am glucose and an
//  8pm glucose are different measurements, and morning versus evening is the point of
//  a dose. For a ONE-PER-DAY store (mood, weight, appetite level, water level) the
//  time is an artifact of when the owner happened to pick up the phone. "Night 0 ·
//  Morning 4 · Afternoon 0 · Evening 0" under four weigh-ins says nothing about the
//  cat; "Afternoon 1 · Evening 17" under mood says only that this owner opens the app
//  after dinner.
//
//  This is NOT the "every kind or none" rule being weakened. That rule exists to stop
//  SEIZURES being singled out for a live counter the owner can break: it is about not
//  making one record special in a way that implies judgement. It was never a rule that
//  every record must display every available statistic. Uniformity still holds WITHIN
//  each shape, which is what the principle actually required: every event store gets
//  the row, no one-per-day store does, and no surface may decide otherwise per record.
//
//  A record with BOTH stores (water, appetite) states the row as soon as it has
//  measured events, because it then genuinely has entries whose moment is a datum.
//   • No streaks, runs, "days since", "X-free for N days", completion percentages
//     or adherence scores (AI/domain.md bans these outright, and this is the most
//     likely place to reintroduce one by accident).
//
//  It is a READ MODEL: nothing here is stored and nothing travels back into an
//  entry store. Being a read model rather than a screen is what makes "uniform
//  across kinds" true by construction rather than by discipline, and that is the
//  only reason a facts panel is doctrinally allowed at all.
//
//  MAUI-free on purpose, like CelestialEvent: the arithmetic is compile-linked
//  into the test project so the band boundaries can be proven without a device.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The four fixed bands a day is divided into. Fixed, named, and always all
/// four: see the file header.</summary>
public enum DayPart
{
    /// <summary>00:00–06:00.</summary>
    Night,

    /// <summary>06:00–12:00.</summary>
    Morning,

    /// <summary>12:00–18:00.</summary>
    Afternoon,

    /// <summary>18:00–24:00.</summary>
    Evening
}

/// <summary>
/// How many entries fell in each band. <b>All four are always present</b>, including
/// the zeros: a band omitted because it was empty, or moved because it was busy,
/// would be the app choosing what to tell the owner.
/// </summary>
public readonly record struct DayPartCounts(int Night, int Morning, int Afternoon, int Evening)
{
    /// <summary>The four, in fixed order. Every renderer walks this, never a
    /// hand-written sequence that could be sorted or filtered on the way past.</summary>
    public IReadOnlyList<int> InOrder => new[] { Night, Morning, Afternoon, Evening };

    /// <summary>Sums to the entry count. The bands are half-open and exhaustive, so
    /// this is an identity, not an approximation.</summary>
    public int Total => Night + Morning + Afternoon + Evening;

    /// <summary>Which band a time of day falls in. Half-open, lower bound inclusive:
    /// 06:00 is Morning, not Night; midnight is Night.</summary>
    public static DayPart Of(TimeSpan timeOfDay)
    {
        var hour = (int)timeOfDay.TotalHours;
        if (hour < 6) return DayPart.Night;
        if (hour < 12) return DayPart.Morning;
        if (hour < 18) return DayPart.Afternoon;
        return DayPart.Evening;
    }

    /// <summary>Count a set of moments into the four bands.</summary>
    public static DayPartCounts From(IEnumerable<TimeSpan> timesOfDay)
    {
        int night = 0, morning = 0, afternoon = 0, evening = 0;
        foreach (var t in timesOfDay)
        {
            switch (Of(t))
            {
                case DayPart.Night: night++; break;
                case DayPart.Morning: morning++; break;
                case DayPart.Afternoon: afternoon++; break;
                default: evening++; break;
            }
        }
        return new DayPartCounts(night, morning, afternoon, evening);
    }

    /// <summary>AppStrings key for a band's name: a KEY, never a resolved string, so a
    /// live language switch reaches it (AI/coding-standards.md).</summary>
    public static string LabelKey(DayPart part) => part switch
    {
        DayPart.Night => "Facts_Night",
        DayPart.Morning => "Facts_Morning",
        DayPart.Afternoon => "Facts_Afternoon",
        _ => "Facts_Evening",
    };
}

/// <summary>
/// What happened to the doses in a range, in the app's own vocabulary.
///
/// <para><b>"Not recorded", never "missed."</b> People caring for a chronically ill
/// animal are already carrying guilt and the app never adds to it: a dose nobody
/// answered is a dose nobody answered (AI/app-voice.md §9). It covers both a slot with
/// no log at all and a log the reconciliation sweep marked; from the owner's side
/// those are the same thing.</para>
///
/// <para>Three raw counts and nothing derived: no percentage, no adherence score, no
/// run of consecutive anything.</para>
/// </summary>
public readonly record struct DoseCounts(int Given, int Skipped, int NotRecorded)
{
    public int Total => Given + Skipped + NotRecorded;
}

/// <summary>One thing written down: when, and: for a record that carries a number,
/// what it read. <paramref name="Value"/> is null for everything qualitative; a stored
/// 1–5 observation level must NEVER arrive here, because that would turn a word into a
/// number the moment something took its minimum.</summary>
public readonly record struct RecordMoment(DateTime When, decimal? Value);

/// <summary>
/// One relative reading, on the day it was written down.
///
/// <para>A SEPARATE type from <see cref="RecordMoment"/>, and that separation is the
/// rule rather than tidiness: <paramref name="Level"/> is a row index on a word-labelled
/// axis, never a value. It picks which labelled row a mark sits on and is never shown,
/// summed, averaged or trended, which is exactly why it cannot travel in a
/// <c>RecordMoment</c>, where something would eventually take its minimum and print
/// "Lowest 2" (AI/design-decisions.md → "Communication layer, not interpretation
/// layer").</para>
/// </summary>
public readonly record struct RecordObservation(DateTime When, int Level);

/// <summary>
/// What one record says about a stretch of time, stated as counts and dates.
/// </summary>
/// <param name="Kind">Which record: the same key Today's cards use, which is the one
/// identity covering the six shipped trackers, medication doses and the owner's own.</param>
/// <param name="Count">Entries in range: rows for an event store, DAYS RECORDED for a
/// one-per-day store (mood, weight, appetite level, water level), doses for medication.</param>
/// <param name="Lowest">The lowest number written down in the range, for the kinds that
/// carry one (weight, glucose, water mL, appetite grams, a custom Amount). Null
/// otherwise, and never derived from an observation level.</param>
/// <param name="Latest">The most recent number in the range, with <paramref name="LatestOn"/>
/// as its date. A recorded value and when it was recorded; nothing is inferred from it.</param>
/// <param name="Doses">Medication only. Null for every other kind.</param>
/// <param name="StatesDayParts">Whether the four bands are worth stating: see the
/// file header. Decided by the SHAPE OF THE STORE the moments came from, in the
/// builder, so no surface can answer it differently for one record.</param>
public sealed record RecordFacts(
    TodayCardKey Kind,
    DateTime From,
    DateTime To,
    int Count,
    DayPartCounts DayParts,
    decimal? Lowest = null,
    decimal? Highest = null,
    decimal? Latest = null,
    DateTime? LatestOn = null,
    DateTime? FirstOn = null,
    DateTime? LastOn = null,
    DoseCounts? Doses = null,
    bool StatesDayParts = false)
{
    public bool HasAny => Count > 0;

    /// <summary>This record carries numbers, and there are some in the range.</summary>
    public bool HasValues => Lowest is not null && Highest is not null;

    /// <summary>Nothing written down for this record in this range. A zero-count
    /// snapshot rather than null: "you wrote nothing down" is itself a fact about the
    /// record, and every caller would otherwise need a second empty path.</summary>
    public static RecordFacts Empty(TodayCardKey kind, DateTime from, DateTime to) =>
        new(kind, from, to, 0, default);
}

/// <summary>
/// Everything one record has to say about a range: the facts, and the marks a surface
/// can draw. Gathered in ONE pass, because the reads behind them are identical: the
/// facts service was already fetching exactly these moments and throwing them away into
/// the builder.
///
/// <para>The three lists are what decides how a record is DRAWN; there is deliberately
/// no shape enum, because the shape is a fact about the data rather than a table someone
/// has to keep in step with it. A record with measurements gets a line, one with
/// observations gets a word-labelled ribbon, one with neither gets marks at the moments
/// it happened, and a record with both (water, appetite) gets two separate charts,
/// never one merged one.</para>
/// </summary>
/// <param name="Measured">Readings carrying a number: kg, mmol/L, mL, grams, a custom
/// Amount.</param>
/// <param name="Observed">Relative readings, as labelled rows. Never numbers.</param>
/// <param name="Events">Occurrences with no value at all: a seizure, a Tick tracker.
/// Position is when it happened, and nothing else is encoded.</param>
public sealed record RecordSnapshot(
    RecordFacts Facts,
    IReadOnlyList<RecordMoment> Measured,
    IReadOnlyList<RecordObservation> Observed,
    IReadOnlyList<RecordMoment> Events)
{
    public bool HasMeasured => Measured.Count > 0;
    public bool HasObserved => Observed.Count > 0;
    public bool HasEvents => Events.Count > 0;

    public static RecordSnapshot Empty(RecordFacts facts) => new(
        facts,
        Array.Empty<RecordMoment>(),
        Array.Empty<RecordObservation>(),
        Array.Empty<RecordMoment>());
}

/// <summary>
/// Turns moments into a <see cref="RecordFacts"/>. Pure, and separate from the service
/// that fetches them so the two rules most easily broken can be proven: a one-per-day
/// store counts DAYS not rows, and the four bands are half-open and total to the count.
/// </summary>
public static class RecordFactsBuilder
{
    /// <summary>
    /// Build the snapshot.
    /// </summary>
    /// <param name="events">Moments where every row counts: an event store.</param>
    /// <param name="perDay">Moments from a one-per-day store. Collapsed to one per
    /// calendar date here rather than by the caller, so "counts days, not rows" holds
    /// however the rows arrive (a revived tombstone, a legacy duplicate, a pull that
    /// raced its own natural-key merge).</param>
    /// <param name="doses">Medication's three counts, or null.</param>
    public static RecordFacts Build(
        TodayCardKey kind,
        DateTime from,
        DateTime to,
        IEnumerable<RecordMoment> events,
        IEnumerable<RecordMoment> perDay,
        DoseCounts? doses = null)
    {
        var all = new List<RecordMoment>(events);

        // Whether the four bands mean anything, decided HERE and by the shape of the
        // store the moments arrived from: an event's time is a fact about the pet, a
        // one-per-day row's is a fact about when the owner picked up the phone. Doses
        // count as events: morning versus evening is the point of them. See the file
        // header for why this is not the "every kind or none" rule being weakened.
        var statesDayParts = all.Count > 0 || doses is not null;

        // One per calendar date, earliest kept: the shape those stores guarantee, made
        // structural here instead of assumed.
        foreach (var group in perDay.GroupBy(m => m.When.Date))
            all.Add(group.OrderBy(m => m.When).First());

        if (all.Count == 0)
            return RecordFacts.Empty(kind, from, to) with { Doses = doses };

        all.Sort((a, b) => a.When.CompareTo(b.When));

        var numbers = all.Where(m => m.Value is not null).ToList();
        var lastNumber = numbers.Count > 0 ? numbers[^1] : (RecordMoment?)null;

        return new RecordFacts(
            kind,
            from,
            to,
            all.Count,
            DayPartCounts.From(all.Select(m => m.When.TimeOfDay)),
            Lowest: numbers.Count > 0 ? numbers.Min(m => m.Value!.Value) : null,
            Highest: numbers.Count > 0 ? numbers.Max(m => m.Value!.Value) : null,
            Latest: lastNumber?.Value,
            LatestOn: lastNumber?.When.Date,
            FirstOn: all[0].When.Date,
            LastOn: all[^1].When.Date,
            Doses: doses,
            StatesDayParts: statesDayParts);
    }
}
