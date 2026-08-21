namespace Animal_Diary_App.Data.ViewModels;

using System.Globalization;
using System.Windows.Input;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.View.Controls;
using Animal_Diary_App.Helpers;

// ─────────────────────────────────────────────────────────────────────────────
//  Today's look-back half.
//
//  It used to be two hardcoded surfaces: a 30-day mood ribbon and a weight line
//  chart: on a page whose top half already followed the pet. So an owner managing
//  a diabetic cat got a glucose card at the top, scrolled down, and found charts of
//  the two things they cared least about.
//
//  This section reads the CARE PLAN instead: one block per record the owner already
//  asked the app to track, in fixed catalog order. That is the whole design and the
//  reason it does not turn Today into a dashboard: it adds no knob, no preference,
//  no picker. The plan is a list the owner curated on Manage; Today simply stops
//  hardcoding two entries from it.
//
//  Order is fixed and every record with entries gets a block, including the quiet
//  ones. Ordering by volume, or dropping the ones with little in them, would be the
//  app deciding which record matters (Data/Models/RecordFacts.cs).
//
//  THIS IS THE ONE PLACE TODAY STATES FACTS ABOUT A RECORD. The card picker used to
//  state them too, above its rows; a fact you must tap a card to discover is a fact
//  most owners never see, and two homes for the same panel is how they drift apart.
//  If facts belong somewhere new on this page, they move here: they do not get a
//  second home.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One band's entry in a split chart's key: which part of the day it holds, inked
/// exactly as its line is.
///
/// <para>Without it a split chart is two unlabelled lines. The key NAMES the bands and
/// stops: it carries no counts, no ordering by size, and nothing suggesting one band
/// matters more than another; it is the same fixed vocabulary, in the same fixed order,
/// that the day-part counts use.</para>
/// </summary>
public sealed record SeriesKey(string Label, Color Ink);

/// <summary>
/// One dated label under a block's charts, positioned proportionally so it sits over the
/// day it names.
/// </summary>
/// <param name="Bounds">An <c>AbsoluteLayout</c> rect with a proportional X and
/// auto-sized extent, so MAUI anchors the label by its own width: the first and last
/// ticks tuck against the edges instead of overflowing them.</param>
public sealed record AxisTick(string Label, Rect Bounds);

/// <summary>
/// One record's block: its name, whatever it can honestly be drawn as, and its facts.
///
/// <para>A block carries whichever of the three visual forms its data supports, and a
/// record with <b>both</b> measurements and observations (water, appetite) carries two,
/// never one merged chart. That is the report's rule applied to Today
/// (AI/design-decisions.md → "Communication layer, not interpretation layer").</para>
/// </summary>
public sealed class LookBackBlock
{
    public required string Name { get; init; }
    public required string Icon { get; init; }

    // ── Measured: a line over time ──
    public SeriesChartDrawable? Chart { get; init; }
    public bool HasChart => Chart is not null;

    /// <summary>Which line is which, when the chart was split by day-part. Empty for a
    /// record drawn whole: there is only one line and it needs no name.</summary>
    public IReadOnlyList<SeriesKey> ChartKey { get; init; } = Array.Empty<SeriesKey>();
    public bool HasChartKey => ChartKey.Count > 0;

    // ── Observed: the ribbon, one mark per reading, placed by date ──
    public ObservationStripDrawable? Observations { get; init; }
    public bool HasObservations => Observations is not null;

    /// <summary>This record holds BOTH measurements and observations (water, appetite),
    /// so the two charts above need naming, otherwise they read as one picture, which
    /// is the merge the rule exists to prevent.</summary>
    public bool IsDualStore => HasChart && HasObservations;

    // ── Events: marks at the moments they happened ──
    public EventStripDrawable? Events { get; init; }
    public bool HasEvents => Events is not null;

    /// <summary>The dated labels under the charts. ONE axis per block, however many
    /// charts it has: they cover the same stretch, and a second axis saying the same
    /// thing would suggest they did not.</summary>
    public IReadOnlyList<AxisTick> Axis { get; init; } = Array.Empty<AxisTick>();
    public bool HasAxis => Axis.Count > 0;

    // ── The facts, in the same words every other surface states them ──
    public required string Count { get; init; }

    /// <summary>Empty for a one-per-day record, where the entry's time says when the
    /// owner picked up the phone rather than anything about the pet
    /// (Data/Models/RecordFacts.cs).</summary>
    public required string DayParts { get; init; }
    public bool HasDayParts => DayParts.Length > 0;

    public string Values { get; init; } = string.Empty;
    public bool HasValues => Values.Length > 0;
}

public class TodayLookBackViewModel : BaseViewModel
{
    private readonly ActivePetService _activePet;
    private readonly CarePlanService _carePlan;
    private readonly RecordFactsService _facts;
    private readonly CustomTrackerService _custom;
    private readonly TodayCardService _cards;
    private readonly MedicationService _medications;

    private int _loadGeneration;

    public TodayLookBackViewModel(
        ActivePetService activePet,
        CarePlanService carePlan,
        RecordFactsService facts,
        CustomTrackerService custom,
        TodayCardService cards,
        MedicationService medications)
    {
        _activePet = activePet;
        _carePlan = carePlan;
        _facts = facts;
        _custom = custom;
        _cards = cards;
        _medications = medications;

        SetRangeCommand = new Command<string>(async days => await SetRangeAsync(days));
    }

    private static LocalizationManager Loc => LocalizationManager.Instance;

    public ICommand SetRangeCommand { get; }

    public string Heading => Loc.GetString("Today_LookingBack");

    /// <summary>Names the marks and stops. Not "what changed and what it did".</summary>
    public string ChangesHeading => Loc.GetString("Today_WhatChanged");

    /// <summary>Default 30 days rather than 90: this is the page someone opens every
    /// morning, and the cost of the section is one range read per record in the plan.</summary>
    private int _rangeDays = 30;

    public int RangeDays
    {
        get => _rangeDays;
        private set => SetProperty(ref _rangeDays, value);
    }

    /// <summary>The blocks, in fixed catalog order. A <see cref="RangeObservableCollection{T}"/>
    /// because Today is a tab page and this list is rendered by <c>BindableLayout</c>,
    /// which has no virtualization: <c>Clear()</c> plus a per-item <c>Add()</c> builds a
    /// template and invalidates layout once per row (AI/coding-standards.md).
    ///
    /// <para>The offered stretches are 14 / 30 / 90 / 365 days, named in the XAML's four
    /// range tabs. Deliberately capped at a year: this section reads one range per record
    /// in the plan, and an "all time" option would turn a nine-tracker pet's Today into
    /// nine decade-wide scans.</para></summary>
    public RangeObservableCollection<LookBackBlock> Blocks { get; } = new();

    /// <summary>
    /// The treatment changes inside the range, oldest first: "22 May · Phenobarbital
    /// 30 mg → 45 mg".
    ///
    /// <para>Thirty days of glucose across a dose change is two different treatment
    /// regimes drawn as one series with nothing saying so. Each of these has a quiet
    /// vertical mark at its date on every chart in the section; the words live here,
    /// once, because a card-width chart cannot carry the label without shouting and
    /// repeating the same three lines under every block would be noise.</para>
    ///
    /// <para><b>What changed and when. Nothing else.</b> No counts either side, no
    /// shading of the regions, no arrow, no delta, no wording that compares. The app
    /// places the mark; the owner draws the conclusion, and if a label would need to say
    /// more than this, it has gone too far (AI/domain.md).</para>
    /// </summary>
    public RangeObservableCollection<string> Changes { get; } = new();

    private bool _hasChanges;
    public bool HasChanges { get => _hasChanges; private set => SetProperty(ref _hasChanges, value); }

    private bool _hasBlocks;

    /// <summary>Nothing in the plan has anything in this range: the whole section is
    /// absent rather than showing a column of empty charts. Same rule as the
    /// Constellation's legend: a key to what is actually there.</summary>
    public bool HasBlocks { get => _hasBlocks; private set => SetProperty(ref _hasBlocks, value); }

    private async Task SetRangeAsync(string? days)
    {
        if (!int.TryParse(days, out var value) || value == RangeDays)
            return;

        RangeDays = value;
        await LoadAsync();
    }

    /// <summary>
    /// Rebuild every block for the active pet.
    ///
    /// <para>Reads run one at a time, never in a <c>Task.WhenAll</c>: sqlite-net queues
    /// each call to the thread pool where it locks the one shared connection, so
    /// concurrency here would occupy N threads to run N queries in sequence anyway
    /// (AI/coding-standards.md). Today's own freshness guard stops this re-running on
    /// every appearance.</para>
    /// </summary>
    public async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        var pet = _activePet.ActivePet;

        if (pet is null || pet.Id == 0)
        {
            Blocks.ReplaceAll(Array.Empty<LookBackBlock>());
            HasBlocks = false;
            return;
        }

        var to = DateTime.Now.Date;
        var from = to.AddDays(-(RangeDays - 1));

        var plan = await _carePlan.GetPlanAsync(pet);

        // The archived-inclusive definitions, for names: a tracker retired last month
        // still recorded things inside this range, and a block with no name would be
        // worse than no block at all.
        var definitions = (await _custom.GetAllForPetAsync(pet.Id)).ToDictionary(c => c.Id);

        // Which two records the owner put on the stat cards. A card IS "the last reading
        // and when", so the block for a carded record drops its own "Latest" clause,
        // otherwise Today states one fact twice, eight hundred pixels apart, and the
        // owner configured one of the two by hand. Lowest and Highest stay on every
        // record: no card states those.
        var config = await _cards.GetConfigAsync(pet);

        // The treatment ledger over the range. Widened by a day at each end rather than
        // converted, exactly as the appointment summary does it: the ledger is stamped in
        // UTC and the range is local dates, and an hour's slop cannot move a change onto
        // the wrong side of a marker anyone would notice.
        var changes = await _medications.GetChangesForRangeAsync(
            pet.Id, from.AddDays(-1).ToUniversalTime(), to.AddDays(2).ToUniversalTime());

        var span = Span(from, to);
        var markers = changes
            .Select(c => Fraction(LedgerText.LocalDate(c), from, span))
            .Where(x => x >= 0 && x <= 1)
            .ToList();

        // Fixed catalog order, not care-plan order: the plan's own order is an
        // implementation detail of how it was seeded, and a section that reshuffles
        // itself when a tracker is added looks ranked.
        var ordered = new List<(TodayCardKey Card, TrackerKey Tracker)>(plan.Count);
        foreach (var item in plan)
        {
            // A tracker with no card key is one no version of this app records: it
            // cannot have a snapshot either, so there is nothing to draw.
            if (TodayCardKey.From(item.Key) is TodayCardKey card)
                ordered.Add((card, item.Key));
        }
        ordered.Sort((a, b) => TodayCardCatalog.Order(a.Card).CompareTo(TodayCardCatalog.Order(b.Card)));

        var blocks = new List<LookBackBlock>(ordered.Count);
        foreach (var (card, tracker) in ordered)
        {
            var snapshot = await _facts.GetSnapshotAsync(pet, card, from, to);
            if (!snapshot.Facts.HasAny)
                continue;                   // nothing written down about it in this range

            var onCard = card == config.Primary || card == config.Secondary;
            blocks.Add(BuildBlock(card, tracker, definitions, snapshot, from, to, onCard, markers));
        }

        // A newer load (a pet switch, a range change) owns the section now.
        if (generation != _loadGeneration)
            return;

        // Built completely, then swapped in ONE notification, never cleared across an
        // await, and never refilled item by item (AI/coding-standards.md).
        Blocks.ReplaceAll(blocks);
        HasBlocks = blocks.Count > 0;

        var lines = changes
            .Where(c => LedgerText.LocalDate(c) >= from && LedgerText.LocalDate(c) <= to)
            .Select(LedgerText.Line)
            .ToList();
        Changes.ReplaceAll(lines);
        HasChanges = HasBlocks && lines.Count > 0;

        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(ChangesHeading));
    }

    private LookBackBlock BuildBlock(
        TodayCardKey key,
        TrackerKey trackerKey,
        IReadOnlyDictionary<int, CustomTracker> definitions,
        RecordSnapshot snapshot,
        DateTime from,
        DateTime to,
        bool onCard,
        IReadOnlyList<double> markers)
    {
        var visual = TrackerVisuals.For(trackerKey);
        var accent = AppColors.Resolve(visual.RowInkKey, Colors.SlateGray);

        // A custom tracker is named by the owner, verbatim; a shipped one by its record
        // key ("Glucose"), never the card face's "Last glucose": this block is about a
        // stretch of time, not the last reading. One lookup answers both halves; a
        // definition that has vanished (a hard purge) leaves the generic pair, which is
        // the honest outcome when there is nothing left to name.
        CustomTracker? definition = null;
        if (key.IsCustom)
            definitions.TryGetValue(key.CustomId, out definition);

        var name = !key.IsCustom
            ? TodayCardCatalog.RecordName(key.BuiltIn!.Value)
            : definition?.Name ?? Loc.GetString("Today_CardCustom");

        var icon = !key.IsCustom
            ? visual.Icon
            : definition is null ? CustomTrackerVisuals.DefaultIcon : CustomTrackerVisuals.For(definition).Icon;

        var chart = BuildChart(snapshot, accent, from, to, markers);

        return new LookBackBlock
        {
            Name = name,
            Icon = icon,
            Chart = chart,
            ChartKey = BuildChartKey(chart, accent),
            Observations = BuildObservations(snapshot, key, accent, from, to),
            Events = BuildEvents(snapshot, accent, from, to),
            Axis = BuildAxis(from, to),
            Count = RecordFactsText.CountAndRange(snapshot.Facts, name),
            DayParts = RecordFactsText.DayParts(snapshot.Facts),
            Values = RecordFactsText.Values(snapshot.Facts, includeLatest: !onCard),
        };
    }

    /// <summary>How many days one unit of the horizontal axis covers. Shared by every
    /// drawable in a block AND by the axis under them, so a mark and the date beneath it
    /// can never drift apart.</summary>
    private static double Span(DateTime from, DateTime to) => Math.Max(1, (to - from).TotalDays + 1);

    private static double Fraction(DateTime when, DateTime from, double span) =>
        (when - from).TotalDays / span;

    /// <summary>
    /// The dates along the foot.
    ///
    /// <para>The step is <b>chosen, never computed</b>: the same rule the Constellation's
    /// lettering follows. Dates land on units a person recognises (a week, a fortnight, a
    /// quarter) rather than on "every 23 days", and they are counted back from today, so
    /// the right-hand end of the axis is always a date the owner is thinking in.</para>
    /// </summary>
    private static IReadOnlyList<AxisTick> BuildAxis(DateTime from, DateTime to)
    {
        // Coarsest last. Roughly six "20 Aug" labels fit the width of a card.
        int[] steps = { 1, 2, 7, 14, 30, 91, 182, 365 };
        const int MaxTicks = 6;

        var span = Span(from, to);
        var step = steps.FirstOrDefault(s => span / s <= MaxTicks);
        if (step == 0)
            step = steps[^1];

        var ticks = new List<AxisTick>(MaxTicks);
        for (var day = to; day >= from; day = day.AddDays(-step))
        {
            ticks.Add(new AxisTick(
                day.ToString("d MMM", CultureInfo.CurrentCulture),
                new Rect(Fraction(day, from, span), 0, -1, -1)));   // -1 = AutoSize
        }

        ticks.Reverse();
        return ticks;
    }

    /// <summary>How many readings a day-part band needs before it is worth a line of its
    /// own. With four weigh-ins, splitting produces two lines of two points, which is
    /// worse than one line of four.</summary>
    private const int MinReadingsPerBand = 4;

    /// <summary>How many bands must clear that bar before the record is split at all. One
    /// busy band is not a pattern; two is the twice-daily regimen this exists for.</summary>
    private const int MinBandsToSplit = 2;

    private static SeriesChartDrawable? BuildChart(
        RecordSnapshot snapshot, Color accent, DateTime from, DateTime to,
        IReadOnlyList<double> markers)
    {
        if (!snapshot.HasMeasured)
            return null;

        // Every reading at its own moment, no per-day summing. Five 50 ml entries are
        // five things the owner wrote down, and collapsing them into one 250 ml point
        // would draw a number nobody recorded.
        var span = Span(from, to);
        var ordered = snapshot.Measured.OrderBy(m => m.When).ToList();
        var points = ordered
            .Select(m => new SeriesPoint(Fraction(m.When, from, span), (double)m.Value!.Value))
            .ToList();

        var min = points.Min(p => p.Value);
        var max = points.Max(p => p.Value);

        // A flat series would otherwise sit on the floor of the chart; give it room so it
        // reads as steady rather than as zero.
        if (Math.Abs(max - min) < 0.0001)
        {
            min -= 1;
            max += 1;
        }
        else
        {
            var pad = (max - min) * 0.12;
            min -= pad;
            max += pad;
        }

        return new SeriesChartDrawable
        {
            Series = BuildSeries(ordered, points),
            Markers = markers,
            Min = min,
            Max = max,
            Accent = accent,
        };
    }

    /// <summary>
    /// One line, or one line per day-part band.
    ///
    /// <para>A 7am fasting reading and an 8pm post-meal reading are different
    /// measurements, and a straight segment between them asserts a continuous curve that
    /// does not exist: on a twice-daily insulin regimen the single zigzag is the
    /// difference between an unreadable scribble and the one view that matters. So the
    /// readings are grouped by the four fixed bands the facts panel already counts.</para>
    ///
    /// <para><b>Only when the record actually clusters that way.</b> Two bands must each
    /// hold a handful of readings; otherwise the record is drawn whole. Once it does
    /// split, EVERY band with anything in it gets a line, including the thin ones: a
    /// reading dropped for landing in a quiet band would be a reading the owner wrote
    /// down and the app decided not to show.</para>
    ///
    /// <para>This is not interpretation. It groups by a fact the owner recorded, using a
    /// vocabulary the app already has, and it names no band as the interesting one.</para>
    /// </summary>
    private static IReadOnlyList<SeriesBand> BuildSeries(
        IReadOnlyList<RecordMoment> moments, IReadOnlyList<SeriesPoint> points)
    {
        var byBand = new List<SeriesPoint>[4];
        for (var i = 0; i < byBand.Length; i++)
            byBand[i] = new List<SeriesPoint>();

        for (var i = 0; i < moments.Count; i++)
            byBand[(int)DayPartCounts.Of(moments[i].When.TimeOfDay)].Add(points[i]);

        var clustered = byBand.Count(b => b.Count >= MinReadingsPerBand);
        if (clustered < MinBandsToSplit)
            return new[] { new SeriesBand(-1, points, SeriesChartDrawable.InkFor(-1)) };

        var series = new List<SeriesBand>(4);
        for (var part = 0; part < byBand.Length; part++)
            if (byBand[part].Count > 0)
                series.Add(new SeriesBand(part, byBand[part], SeriesChartDrawable.InkFor(part)));

        return series;
    }

    /// <summary>The key under a split chart: the band names, in the fixed band order,
    /// inked as their lines are. Empty for a record drawn whole.</summary>
    private static IReadOnlyList<SeriesKey> BuildChartKey(SeriesChartDrawable? chart, Color accent)
    {
        if (chart is null || chart.Series.Count < 2)
            return Array.Empty<SeriesKey>();

        return chart.Series
            .Select(band => new SeriesKey(
                Loc.GetString(DayPartCounts.LabelKey((DayPart)band.Part)),
                accent.WithAlpha(band.Ink)))
            .ToList();
    }

    /// <summary>The ribbon: one mark per reading, placed by DATE and heighted by the
    /// stored level. Mood keeps its own per-level colours, which the app already uses
    /// everywhere it shows a mood; every other observation wears its record's single
    /// accent, because inventing a five-colour scale for water would be inventing a
    /// judgement about it.</summary>
    private static ObservationStripDrawable? BuildObservations(
        RecordSnapshot snapshot, TodayCardKey key, Color accent, DateTime from, DateTime to)
    {
        if (!snapshot.HasObserved)
            return null;

        var span = Span(from, to);
        var isMood = key.Is(TodayCardId.Mood);

        return new ObservationStripDrawable
        {
            Marks = snapshot.Observed
                .OrderBy(o => o.When)
                .Select(o => new ObservationMark(
                    Fraction(o.When, from, span),
                    o.Level,
                    isMood ? ((MoodLevel)o.Level).GetColor() : accent.WithAlpha(0.8f)))
                .ToList(),
        };
    }

    private static EventStripDrawable? BuildEvents(
        RecordSnapshot snapshot, Color accent, DateTime from, DateTime to)
    {
        if (!snapshot.HasEvents)
            return null;

        var span = Span(from, to);
        return new EventStripDrawable
        {
            Positions = snapshot.Events
                .OrderBy(m => m.When)
                .Select(m => Fraction(m.When, from, span))
                .ToList(),
            Accent = accent,
        };
    }
}
