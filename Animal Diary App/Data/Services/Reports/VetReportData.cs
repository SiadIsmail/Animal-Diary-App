namespace Animal_Diary_App.Data.Services.Reports;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  DATA layer of the vet report.
//
//  A plain, presentation-free snapshot of everything the report MIGHT show for
//  one pet over one date range. No formatting, no layout, no QuestPDF types,
//  the document layer decides how (and whether) each piece is rendered.
//
//  Hard rule carried by this whole feature: the report REPORTS owner-logged
//  facts. Nothing in here interprets, flags or judges, no severities we
//  invented, no trends we concluded. The vet does the medicine.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Everything the vet report might show. Built by
/// <see cref="VetReportDataBuilder"/> (real data) or
/// <see cref="VetReportSampleData"/> (fake data for layout iteration).</summary>
public sealed class VetReportData
{
    /// <summary>Which of the two documents this snapshot is for. It selects the section
    /// set in <c>VetReportDocument</c> and which half of this DTO is populated: the
    /// designed sections below, or <see cref="PlainLog"/>. Never both.</summary>
    public ReportStyle Style { get; init; } = ReportStyle.Designed;

    public required ReportPetInfo Pet { get; init; }

    /// <summary>Inclusive date range the report covers.</summary>
    public required DateTime From { get; init; }
    public required DateTime To { get; init; }
    public required DateTime GeneratedAt { get; init; }

    public IReadOnlyList<ReportMedication> Medications { get; init; } = Array.Empty<ReportMedication>();

    /// <summary>Time series worth charting (weight, glucose, seizures/week…).
    /// The document renders one small chart per series, in list order.</summary>
    public IReadOnlyList<ReportSeries> Trends { get; init; } = Array.Empty<ReportSeries>();

    /// <summary>Water intake, kept as two DISTINCT data types that are never merged
    /// or interpreted: see <see cref="ReportWater"/>. Felova is a communication
    /// layer, not a medical-interpretation layer.</summary>
    public ReportWater Water { get; init; } = new();

    /// <summary>Appetite, the same measured-vs-observed shape as water plus the diet
    /// list: see <see cref="ReportAppetite"/>. Never merged or interpreted.</summary>
    public ReportAppetite Appetite { get; init; } = new();

    /// <summary>The owner's daily read on how the pet seemed: qualitative only, in the
    /// same shape as the water/appetite observations. See <see cref="ReportMood"/>.</summary>
    public ReportMood Mood { get; init; } = new();

    /// <summary>Notable dated occurrences (seizures, vomiting, very low appetite),
    /// newest first.</summary>
    public IReadOnlyList<ReportEvent> Events { get; init; } = Array.Empty<ReportEvent>();

    /// <summary>Whatever the owner tracks themselves and chose to show a vet. Empty
    /// unless they turned the switch on for at least one tracker: see
    /// <see cref="ReportCustom"/>.</summary>
    public ReportCustom Custom { get; init; } = new();

    /// <summary>Free-text notes the owner wrote in the period, newest first.
    /// (There is no separate "questions for the vet" concept yet: when one is
    /// added, give it its own list here and its own section.)</summary>
    public IReadOnlyList<ReportNote> Notes { get; init; } = Array.Empty<ReportNote>();

    /// <summary>Everything the owner wrote down in the range, flattened into one
    /// time-ordered list. Populated only for <see cref="ReportStyle.Plain"/>; the designed
    /// report never reads it, and the plain export reads nothing else.</summary>
    public IReadOnlyList<ReportLogLine> PlainLog { get; init; } = Array.Empty<ReportLogLine>();

    /// <summary>True when the range contains anything at all beyond the pet's
    /// master data. Used to refuse generating an empty document.</summary>
    /// <summary>
    /// One plain line per record whose values on this document were converted from a unit
    /// the owner recorded them in: <i>"Some values were recorded in kg and are shown
    /// converted to lb."</i> Empty when nothing was converted, which is the normal case.
    ///
    /// <para><b>Only when a conversion actually happened</b>, and only about values inside
    /// this report's range. It is a fact about the record, so it sits inside the report's
    /// owner-facts-only rule rather than against it: a silent unit conversion in a medical
    /// document is exactly the kind of thing that should be stated out loud, and with
    /// units chosen per entry a bare number now genuinely can come from either.</para>
    ///
    /// <para>Rendered in the page footer under the disclaimer, on every page, for the same
    /// reason the disclaimer is: printed vet paperwork gets separated and refiled, and a
    /// converted value on page 3 needs the note on page 3.</para>
    /// </summary>
    public IReadOnlyList<string> UnitNotes { get; init; } = Array.Empty<string>();

    public bool HasAnyData => Style == ReportStyle.Plain
        ? PlainLog.Count > 0
        : Medications.Count > 0 || Trends.Count > 0 || Water.HasContent || Appetite.HasContent
          || Mood.HasContent || Events.Count > 0 || Notes.Count > 0 || Custom.HasContent;
}

/// <summary>
/// The two documents this feature produces, and the line between free and paid.
///
/// <para><b>Portability is free; the artifact is the product.</b> AI/domain.md makes
/// "getting your data out is never blocked" a promise, and <see cref="Plain"/> is what
/// keeps it: everything logged, in order, with dates and times, on every tier forever.
/// That fully satisfies it: you can take your data, always, in a form a vet can read.
/// <see cref="Designed"/> is the work done ON that data: sections, charts, the
/// measured-vs-observed separation, the treatment ledger, the front sheet. That is not
/// your data, and it is the paid one.</para>
///
/// <para>Both render through the SAME document layer and the same PDF stack: one
/// <c>VetReportDocument</c>, one section interface, one renderer. There is deliberately
/// no second PDF path: a parallel implementation is how the free export quietly rots
/// while nobody is watching, and the PDF stack must stay free of native libraries
/// (AI/known-constraints.md).</para>
/// </summary>
public enum ReportStyle
{
    /// <summary>The full report: every section, charts included. Paid.</summary>
    Designed,
    /// <summary>The plain chronological log. Free forever, on every tier.</summary>
    Plain
}

/// <summary>
/// One thing the owner wrote down, as the plain export prints it.
/// </summary>
/// <param name="When">Local date and time it was recorded at.</param>
/// <param name="HasTime">False for a legacy mood/weight row saved before per-entry times
/// existed. Those sit at the start of their day, and the export prints the date alone
/// rather than claiming midnight: the app does not invent a moment it was never told.</param>
/// <param name="What">The kind, in the owner's language, or an owner-defined tracker's
/// own name (verbatim user text, never translated).</param>
/// <param name="Detail">The reading exactly as it was written down, or empty.</param>
public readonly record struct ReportLogLine(DateTime When, bool HasTime, string What, string Detail);

/// <summary>
/// The owner's daily read on how their pet seemed. Purely qualitative, and held the
/// same way as water and appetite OBSERVATIONS, never a number, never averaged,
/// never trended. The level only picks which labelled row the dot sits on.
///
/// Mood is one of the two trackers every pet gets by default, so leaving it out of
/// the report meant an owner who logged faithfully every day was still told nothing
/// had been written down. It earns a place here on the same grounds as appetite
/// observations: subjective, but it is what a vet asks about first.
/// </summary>
public sealed class ReportMood
{
    public IReadOnlyList<ReportObservation> Observations { get; init; } = Array.Empty<ReportObservation>();

    public bool HasContent => Observations.Count > 0;
}

/// <summary>
/// Water intake, held as TWO DISTINCT data types that the report keeps apart on
/// purpose: Felova relays what the owner recorded, it does not interpret it:
/// <list type="bullet">
/// <item><b>Measured</b>: objective millilitre readings (a day's total). A
///   quantitative series, plotted as a graph.</item>
/// <item><b>Observations</b>: subjective owner readings ("Normal", "More than
///   usual"). A qualitative series, plotted on its OWN graph.</item>
/// </list>
/// The two are NEVER merged into one visualization, observations are NEVER converted
/// to numbers, and NO trend/verdict is computed from either. Mixing a subjective
/// observation with a measurement, or drawing a conclusion, needs clinical context
/// Felova doesn't have: the vet interprets; the report only records. Either type can
/// be turned off in the export sheet (both default on); an off type is simply null/empty.
/// </summary>
public sealed class ReportWater
{
    /// <summary>Objective measured intake: one point per day (that day's total mL).
    /// Null when the owner logged no measurements, or unticked "measured values".</summary>
    public ReportSeries? Measured { get; init; }

    /// <summary>Subjective owner observations, one per day. Empty when none were
    /// logged, or the owner unticked "owner observations".</summary>
    public IReadOnlyList<ReportObservation> Observations { get; init; } = Array.Empty<ReportObservation>();

    public bool HasContent => Measured is { Points.Count: > 0 } || Observations.Count > 0;
}

/// <summary>One owner observation on a date: a relative level (1..5). Used for both
/// water and appetite. The document plots it on a category axis LABELLED WITH WORDS,
/// the number is never shown, averaged, or trended (it only picks which labelled row
/// the dot sits on).</summary>
public readonly record struct ReportObservation(DateTime Date, int Level);

/// <summary>
/// Appetite for the report: the same measured-vs-observed separation as
/// <see cref="ReportWater"/>, plus the diet list. All three parts are kept distinct
/// and none is interpreted (no trend, no verdict, observations never numeric):
/// <list type="bullet">
/// <item><b>Measured</b>: objective grams eaten, one point per day (that day's
///   total). Null when none, or the owner unticked "measured values".</item>
/// <item><b>Observations</b>: the qualitative reading (Didn't eat … Everything),
///   one per day. Empty when none, or the owner unticked "observations".</item>
/// <item><b>Foods</b>: the distinct free-text foods recorded in the range, as a
///   plain diet list. Not food-change tracking; the range itself is the context.</item>
/// </list>
/// </summary>
public sealed class ReportAppetite
{
    public ReportSeries? Measured { get; init; }
    public IReadOnlyList<ReportObservation> Observations { get; init; } = Array.Empty<ReportObservation>();
    public IReadOnlyList<string> Foods { get; init; } = Array.Empty<string>();

    public bool HasContent =>
        Measured is { Points.Count: > 0 } || Observations.Count > 0 || Foods.Count > 0;
}

/// <summary>Master data for the report header. Fields the app doesn't model yet
/// (owner, breed, sex, photo) are nullable: the header simply omits them.</summary>
public sealed class ReportPetInfo
{
    public required string Name { get; init; }

    /// <summary>Species / pet type, already resolved to a display word ("Dog").</summary>
    public required string Species { get; init; }
    public int? AgeYears { get; init; }
    public string? Breed { get; init; }
    public string? Sex { get; init; }
    public string? OwnerName { get; init; }

    /// <summary>Optional passport-style ID photo. Null = no photo row in the header.</summary>
    public string? PhotoPath { get; init; }

    /// <summary>Condition display names ("Diabetes", "Epilepsy / Seizures").</summary>
    public IReadOnlyList<string> Conditions { get; init; } = Array.Empty<string>();

    /// <summary>Most recent weight in the range (or before it), and the change
    /// across the range, both in KILOGRAMS. Null when the pet has no weight entries.</summary>
    public decimal? CurrentWeightKg { get; init; }
    public decimal? WeightChangeKg { get; init; }

    /// <summary>The unit the two above are to be SHOWN in: the majority of what the owner
    /// actually typed (AI/domain.md, Units). The values stay canonical and the header
    /// converts them through <c>UnitText</c>, which is the text layer and the one
    /// conversion point. Never null: weight always has a resolvable unit.</summary>
    public UnitDef WeightUnit { get; init; } = UnitCatalog.Canonical(UnitFamily.Weight);
}

/// <summary>One medication with its schedule shape and adherence counts over the
/// period. Counts are facts (rows counted), never judgements.</summary>
public sealed class ReportMedication
{
    public required string Name { get; init; }

    /// <summary>The dose as the medication row stands NOW. Kept because it is what a
    /// report with no ledger behind it can honestly say, and it is the fallback
    /// <see cref="DoseText"/> is rendered against.</summary>
    public decimal Dose { get; init; }
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// The dose that was actually in force over the PERIOD, resolved from the treatment
    /// ledger: <c>"30 mg"</c>, or <c>"30 mg → 45 mg"</c> when it changed inside it.
    /// Empty when the ledger has nothing (pre-ledger history), and the renderer then
    /// falls back to <see cref="Dose"/>.
    ///
    /// <para><b>Why this exists.</b> A report for March generated in August used to print
    /// August's dose against March's counts. The report already counts scheduled doses
    /// from the union of rules and logs precisely because "an edit would silently rewrite
    /// history"; this is the other half of that same reasoning.</para>
    ///
    /// <para>The arrow states what changed and when the period was: it is not a
    /// before/after pair of COUNTS around a change, which stays banned. Nothing here
    /// compares the two sides or says which way they went.</para>
    /// </summary>
    public string DoseText { get; init; } = string.Empty;

    /// <summary>Distinct days of week the medication is scheduled on (0–7).</summary>
    public int DaysPerWeek { get; init; }

    /// <summary>Distinct times of day it is scheduled at, sorted.</summary>
    public IReadOnlyList<TimeSpan> TimesOfDay { get; init; } = Array.Empty<TimeSpan>();

    /// <summary>Doses that fell inside the period per the schedule (bounded by the
    /// medication's creation date and today).</summary>
    public int ScheduledCount { get; init; }
    public int TakenCount { get; init; }
    public int SkippedCount { get; init; }
    public int MissedCount { get; init; }
}

/// <summary>
/// A chartable time series: one measured value over time.
///
/// <para><b><see cref="Points"/> are stated in <see cref="Unit"/>, not in the app's
/// canonical unit.</b> This is the one place a value is converted before it reaches the
/// document layer, and deliberately: a chart's axis LABELS are numbers, drawn by the
/// renderer from the point values, so a series whose points said one thing and whose
/// caption said another would put an unlabelled wrong number in front of a vet. The
/// glucose series has always worked this way; weight joined it when units became
/// per-entry.</para>
/// </summary>
public sealed class ReportSeries
{
    /// <summary>What the series is, e.g. "Weight", "Blood glucose", "Seizures per week".</summary>
    public required string Label { get; init; }

    /// <summary>The unit <see cref="Points"/> are in, as a display label ("lb", "mg/dL").
    /// Printed in the caption and beside a single stated value, so it is never absent.</summary>
    public string Unit { get; init; } = string.Empty;
    public required IReadOnlyList<ReportPoint> Points { get; init; }
}

public readonly record struct ReportPoint(DateTime Date, decimal Value);

/// <summary>The kind of a notable event. A closed set the events table knows how
/// to word: extend it here when a new loggable event should reach the report.</summary>
public enum ReportEventKind
{
    Seizure,
    Vomiting,
    /// <summary>An appetite reading of "None" or "Barely" (levels 0–1). Included as
    /// the owner's own low reading: the report states the level, nothing more.</summary>
    LowAppetite
}

/// <summary>One dated occurrence. Typed fields, no prose: the document words it.</summary>
public sealed class ReportEvent
{
    public required ReportEventKind Kind { get; init; }
    public required DateTime Date { get; init; }
    public TimeSpan? Time { get; init; }

    /// <summary>Seizure duration in SECONDS when the owner timed it, null when they
    /// did not. Seconds because an int of minutes could not hold a 45-second seizure,
    /// and sub-minute events are common and clinically relevant.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>The unit the duration is to be SHOWN in, resolved from the owner's own
    /// entries. Never null for a seizure; the document converts through
    /// <c>UnitText</c>.</summary>
    public UnitDef DurationUnit { get; init; } = UnitCatalog.Canonical(UnitFamily.Duration);

    /// <summary>What kind of seizure the owner said it was, or null when they didn't say.
    /// Carried through verbatim: the report states the owner's own answer and never
    /// derives one from the duration or the note.</summary>
    public Models.SeizureType? SeizureType { get; init; }

    /// <summary>Owner's own words (e.g. post-seizure note). Rendered verbatim.</summary>
    public string? Note { get; init; }

    /// <summary>Kind-specific value: the appetite level (0–5) for LowAppetite.</summary>
    public int? Value { get; init; }
}

/// <summary>One free-text note the owner wrote (currently the journal's mood note).</summary>
public sealed record ReportNote(DateTime Date, string Text);

/// <summary>
/// The trackers the owner defined AND chose to show a vet, with what they recorded in
/// the range.
///
/// <para>Deliberately NOT folded into <see cref="ReportEvent"/>: that is a closed
/// <see cref="ReportEventKind"/> the document knows how to word, and these are named by
/// the owner. A custom tracker carries its own label, printed verbatim like a pet or
/// medication name: the document translates nothing here.</para>
///
/// <para>Trackers whose switch is off never reach this object at all, so nothing
/// downstream has to remember to filter. See <see cref="Models.CustomTracker.IncludeInReport"/>.</para>
/// </summary>
public sealed class ReportCustom
{
    public IReadOnlyList<ReportCustomTracker> Trackers { get; init; } = Array.Empty<ReportCustomTracker>();

    /// <summary>Every entry across those trackers, newest first: the section prints one
    /// dated table rather than a block per tracker, so a vet reads them in the order they
    /// happened.</summary>
    public IReadOnlyList<ReportCustomEntry> Entries { get; init; } = Array.Empty<ReportCustomEntry>();

    public bool HasContent => Entries.Count > 0;
}

/// <summary>One owner-defined tracker that reached the report, with how many times it
/// was recorded in the range. The count is a fact (rows counted), which is the only kind
/// of arithmetic this section does.</summary>
public sealed record ReportCustomTracker(string Name, string Unit, int Count);

/// <summary>One dated occurrence of an owner-defined tracker. <paramref name="Name"/> and
/// <paramref name="Unit"/> are the owner's own words and are printed verbatim.</summary>
public sealed record ReportCustomEntry(
    string Name,
    string Unit,
    DateTime Date,
    TimeSpan? Time,
    decimal? Amount,
    string? Note);
