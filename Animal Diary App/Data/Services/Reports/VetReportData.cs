namespace Animal_Diary_App.Data.Services.Reports;

// ─────────────────────────────────────────────────────────────────────────────
//  DATA layer of the vet report.
//
//  A plain, presentation-free snapshot of everything the report MIGHT show for
//  one pet over one date range. No formatting, no layout, no QuestPDF types —
//  the document layer decides how (and whether) each piece is rendered.
//
//  Hard rule carried by this whole feature: the report REPORTS owner-logged
//  facts. Nothing in here interprets, flags or judges — no severities we
//  invented, no trends we concluded. The vet does the medicine.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Everything the vet report might show. Built by
/// <see cref="VetReportDataBuilder"/> (real data) or
/// <see cref="VetReportSampleData"/> (fake data for layout iteration).</summary>
public sealed class VetReportData
{
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
    /// or interpreted — see <see cref="ReportWater"/>. Felova is a communication
    /// layer, not a medical-interpretation layer.</summary>
    public ReportWater Water { get; init; } = new();

    /// <summary>Appetite, the same measured-vs-observed shape as water plus the diet
    /// list — see <see cref="ReportAppetite"/>. Never merged or interpreted.</summary>
    public ReportAppetite Appetite { get; init; } = new();

    /// <summary>Notable dated occurrences (seizures, vomiting, very low appetite),
    /// newest first.</summary>
    public IReadOnlyList<ReportEvent> Events { get; init; } = Array.Empty<ReportEvent>();

    /// <summary>Free-text notes the owner wrote in the period, newest first.
    /// (There is no separate "questions for the vet" concept yet — when one is
    /// added, give it its own list here and its own section.)</summary>
    public IReadOnlyList<ReportNote> Notes { get; init; } = Array.Empty<ReportNote>();

    /// <summary>True when the range contains anything at all beyond the pet's
    /// master data. Used to refuse generating an empty document.</summary>
    public bool HasAnyData =>
        Medications.Count > 0 || Trends.Count > 0 || Water.HasContent || Appetite.HasContent
        || Events.Count > 0 || Notes.Count > 0;
}

/// <summary>
/// Water intake, held as TWO DISTINCT data types that the report keeps apart on
/// purpose — Felova relays what the owner recorded, it does not interpret it:
/// <list type="bullet">
/// <item><b>Measured</b> — objective millilitre readings (a day's total). A
///   quantitative series, plotted as a graph.</item>
/// <item><b>Observations</b> — subjective owner readings ("Normal", "More than
///   usual"). A qualitative series, plotted on its OWN graph.</item>
/// </list>
/// The two are NEVER merged into one visualization, observations are NEVER converted
/// to numbers, and NO trend/verdict is computed from either. Mixing a subjective
/// observation with a measurement, or drawing a conclusion, needs clinical context
/// Felova doesn't have — the vet interprets; the report only records. Either type can
/// be turned off in the export sheet (both default on); an off type is simply null/empty.
/// </summary>
public sealed class ReportWater
{
    /// <summary>Objective measured intake — one point per day (that day's total mL).
    /// Null when the owner logged no measurements, or unticked "measured values".</summary>
    public ReportSeries? Measured { get; init; }

    /// <summary>Subjective owner observations, one per day. Empty when none were
    /// logged, or the owner unticked "owner observations".</summary>
    public IReadOnlyList<ReportObservation> Observations { get; init; } = Array.Empty<ReportObservation>();

    public bool HasContent => Measured is { Points.Count: > 0 } || Observations.Count > 0;
}

/// <summary>One owner observation on a date: a relative level (1..5). Used for both
/// water and appetite. The document plots it on a category axis LABELLED WITH WORDS —
/// the number is never shown, averaged, or trended (it only picks which labelled row
/// the dot sits on).</summary>
public readonly record struct ReportObservation(DateTime Date, int Level);

/// <summary>
/// Appetite for the report — the same measured-vs-observed separation as
/// <see cref="ReportWater"/>, plus the diet list. All three parts are kept distinct
/// and none is interpreted (no trend, no verdict, observations never numeric):
/// <list type="bullet">
/// <item><b>Measured</b> — objective grams eaten, one point per day (that day's
///   total). Null when none, or the owner unticked "measured values".</item>
/// <item><b>Observations</b> — the qualitative reading (Didn't eat … Everything),
///   one per day. Empty when none, or the owner unticked "observations".</item>
/// <item><b>Foods</b> — the distinct free-text foods recorded in the range, as a
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
/// (owner, breed, sex, photo) are nullable — the header simply omits them.</summary>
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
    /// across the range. Null when the pet has no weight entries.</summary>
    public decimal? CurrentWeightKg { get; init; }
    public decimal? WeightChangeKg { get; init; }
}

/// <summary>One medication with its schedule shape and adherence counts over the
/// period. Counts are facts (rows counted), never judgements.</summary>
public sealed class ReportMedication
{
    public required string Name { get; init; }
    public decimal Dose { get; init; }
    public string Unit { get; init; } = string.Empty;

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

/// <summary>A chartable time series: one measured value over time.</summary>
public sealed class ReportSeries
{
    /// <summary>What the series is, e.g. "Weight", "Blood glucose", "Seizures per week".</summary>
    public required string Label { get; init; }
    public string Unit { get; init; } = string.Empty;
    public required IReadOnlyList<ReportPoint> Points { get; init; }
}

public readonly record struct ReportPoint(DateTime Date, decimal Value);

/// <summary>The kind of a notable event. A closed set the events table knows how
/// to word — extend it here when a new loggable event should reach the report.</summary>
public enum ReportEventKind
{
    Seizure,
    Vomiting,
    /// <summary>An appetite reading of "None" or "Barely" (levels 0–1). Included as
    /// the owner's own low reading — the report states the level, nothing more.</summary>
    LowAppetite
}

/// <summary>One dated occurrence. Typed fields, no prose — the document words it.</summary>
public sealed class ReportEvent
{
    public required ReportEventKind Kind { get; init; }
    public required DateTime Date { get; init; }
    public TimeSpan? Time { get; init; }

    /// <summary>Seizure duration when the owner timed it.</summary>
    public int? DurationMinutes { get; init; }

    /// <summary>Owner's own words (e.g. post-seizure note). Rendered verbatim.</summary>
    public string? Note { get; init; }

    /// <summary>Kind-specific value: the appetite level (0–5) for LowAppetite.</summary>
    public int? Value { get; init; }
}

/// <summary>One free-text note the owner wrote (currently the journal's mood note).</summary>
public sealed record ReportNote(DateTime Date, string Text);
