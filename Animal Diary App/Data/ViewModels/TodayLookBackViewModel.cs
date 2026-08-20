namespace Animal_Diary_App.Data.ViewModels;

using System.Collections.ObjectModel;
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
//  It used to be two hardcoded surfaces — a 30-day mood ribbon and a weight line
//  chart — on a page whose top half already followed the pet. So an owner managing
//  a diabetic cat got a glucose card at the top, scrolled down, and found charts of
//  the two things they cared least about.
//
//  This section reads the CARE PLAN instead: one block per record the owner already
//  asked the app to track, in fixed catalog order. That is the whole design and the
//  reason it does not turn Today into a dashboard — it adds no knob, no preference,
//  no picker. The plan is a list the owner curated on Manage; Today simply stops
//  hardcoding two entries from it.
//
//  Order is fixed and every record with entries gets a block, including the quiet
//  ones. Ordering by volume, or dropping the ones with little in them, would be the
//  app deciding which record matters (Data/Models/RecordFacts.cs).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One dated label under a block's charts, positioned proportionally so it sits over the
/// day it names.
/// </summary>
/// <param name="Bounds">An <c>AbsoluteLayout</c> rect with a proportional X and
/// auto-sized extent, so MAUI anchors the label by its own width — the first and last
/// ticks tuck against the edges instead of overflowing them.</param>
public sealed record AxisTick(string Label, Rect Bounds);

/// <summary>
/// One record's block: its name, whatever it can honestly be drawn as, and its facts.
///
/// <para>A block carries whichever of the three visual forms its data supports, and a
/// record with <b>both</b> measurements and observations (water, appetite) carries two —
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

    // ── Observed: the ribbon, one mark per reading, placed by date ──
    public ObservationStripDrawable? Observations { get; init; }
    public bool HasObservations => Observations is not null;

    /// <summary>This record holds BOTH measurements and observations (water, appetite),
    /// so the two charts above need naming — otherwise they read as one picture, which
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
    public required string DayParts { get; init; }
    public string Values { get; init; } = string.Empty;
    public bool HasValues => Values.Length > 0;
}

public class TodayLookBackViewModel : BaseViewModel
{
    private readonly ActivePetService _activePet;
    private readonly CarePlanService _carePlan;
    private readonly RecordFactsService _facts;
    private readonly CustomTrackerService _custom;

    private int _loadGeneration;

    /// <summary>The stretches on offer. Presets, like everywhere else in this app — a
    /// date-range picker is a form. Deliberately capped at a year: this section reads one
    /// range per record in the plan, and an "all time" option would turn a nine-tracker
    /// pet's Today into nine decade-wide scans.</summary>
    public static readonly int[] RangeOptions = { 14, 30, 90, 365 };

    public TodayLookBackViewModel(
        ActivePetService activePet,
        CarePlanService carePlan,
        RecordFactsService facts,
        CustomTrackerService custom)
    {
        _activePet = activePet;
        _carePlan = carePlan;
        _facts = facts;
        _custom = custom;

        SetRangeCommand = new Command<string>(async days => await SetRangeAsync(days));
    }

    private static LocalizationManager Loc => LocalizationManager.Instance;

    public ICommand SetRangeCommand { get; }

    public string Heading => Loc.GetString("Today_LookingBack");

    /// <summary>Default 30 days rather than 90: this is the page someone opens every
    /// morning, and the cost of the section is one range read per record in the plan.</summary>
    private int _rangeDays = 30;

    public int RangeDays
    {
        get => _rangeDays;
        private set => SetProperty(ref _rangeDays, value);
    }

    public ObservableCollection<LookBackBlock> Blocks { get; } = new();

    private bool _hasBlocks;

    /// <summary>Nothing in the plan has anything in this range — the whole section is
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
    /// <para>Reads run one at a time, never in a <c>Task.WhenAll</c> — sqlite-net queues
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
            Blocks.Clear();
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

        // Fixed catalog order, not care-plan order — the plan's own order is an
        // implementation detail of how it was seeded, and a section that reshuffles
        // itself when a tracker is added looks ranked.
        var ordered = plan
            .Select(item => TodayCardKey.From(item.Key) is TodayCardKey k ? (Key: (TodayCardKey?)k, item.Key) : (null, item.Key))
            .Where(x => x.Key is not null)
            .OrderBy(x => CatalogOrder(x.Key!.Value))
            .ToList();

        var blocks = new List<LookBackBlock>(ordered.Count);
        foreach (var (key, trackerKey) in ordered)
        {
            var snapshot = await _facts.GetSnapshotAsync(pet, key!.Value, from, to);
            if (!snapshot.Facts.HasAny)
                continue;                   // nothing written down about it in this range

            blocks.Add(BuildBlock(key.Value, trackerKey, definitions, snapshot, from, to));
        }

        // A newer load (a pet switch, a range change) owns the section now.
        if (generation != _loadGeneration)
            return;

        // Built completely, then swapped in one synchronous block — never cleared across
        // an await (AI/coding-standards.md).
        Blocks.Clear();
        foreach (var block in blocks)
            Blocks.Add(block);

        HasBlocks = blocks.Count > 0;
        OnPropertyChanged(nameof(Heading));
    }

    /// <summary>Position in <see cref="TodayCardCatalog.Cards"/>; the owner's own
    /// trackers follow the shipped ones, in the order they created them.</summary>
    private static int CatalogOrder(TodayCardKey key)
    {
        if (key.IsCustom)
            return 100 + key.CustomId;

        for (var i = 0; i < TodayCardCatalog.Cards.Count; i++)
            if (TodayCardCatalog.Cards[i].Id == key.BuiltIn)
                return i;
        return 99;
    }

    private LookBackBlock BuildBlock(
        TodayCardKey key,
        TrackerKey trackerKey,
        IReadOnlyDictionary<int, CustomTracker> definitions,
        RecordSnapshot snapshot,
        DateTime from,
        DateTime to)
    {
        var visual = TrackerVisuals.For(trackerKey);
        var accent = AppColors.Resolve(visual.RowInkKey, Colors.SlateGray);

        // A custom tracker is named by the owner, verbatim; a shipped one by its record
        // key ("Glucose"), never the card face's "Last glucose" — this block is about a
        // stretch of time, not the last reading.
        var name = key.IsCustom
            ? (definitions.TryGetValue(key.CustomId, out var def) ? def.Name : Loc.GetString("Today_CardCustom"))
            : TodayCardCatalog.RecordName(key.BuiltIn!.Value);

        var icon = key.IsCustom
            ? (definitions.TryGetValue(key.CustomId, out var d2) ? CustomTrackerVisuals.For(d2).Icon : CustomTrackerVisuals.DefaultIcon)
            : visual.Icon;

        return new LookBackBlock
        {
            Name = name,
            Icon = icon,
            Chart = BuildChart(snapshot, accent, from, to),
            Observations = BuildObservations(snapshot, key, accent, from, to),
            Events = BuildEvents(snapshot, accent, from, to),
            Axis = BuildAxis(from, to),
            Count = RecordFactsText.CountAndRange(snapshot.Facts),
            DayParts = RecordFactsText.DayParts(snapshot.Facts),
            Values = RecordFactsText.Values(snapshot.Facts),
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
    /// <para>The step is <b>chosen, never computed</b> — the same rule the Constellation's
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

    private static SeriesChartDrawable? BuildChart(
        RecordSnapshot snapshot, Color accent, DateTime from, DateTime to)
    {
        if (!snapshot.HasMeasured)
            return null;

        // Every reading at its own moment — no per-day summing. Five 50 ml entries are
        // five things the owner wrote down, and collapsing them into one 250 ml point
        // would draw a number nobody recorded.
        var span = Span(from, to);
        var points = snapshot.Measured
            .OrderBy(m => m.When)
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

        return new SeriesChartDrawable { Points = points, Min = min, Max = max, Accent = accent };
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
