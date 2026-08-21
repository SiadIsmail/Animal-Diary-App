namespace Animal_Diary_App.Data.Services;

using System.Globalization;
using Animal_Diary_App.Data.Models;

/// <summary>
/// What changed between two versions of a medication, as ledger rows.
///
/// <para><b>Pure by design.</b> It takes the before and after and hands back rows;
/// it opens nothing and writes nothing. That is what lets the rules below,
/// "an unchanged save writes NO row", "a rename plus a dose change writes two": be
/// tested at all, rather than only being exercised by editing a medication by hand
/// on a device.</para>
///
/// <para>The schedule wording arrives as a delegate rather than being resolved here,
/// so the diff carries no dependency on the localization resources. Production passes
/// <see cref="MedicationScheduleText.Describe"/>, which is the same formatter the
/// medication list uses for its times display.</para>
///
/// <para><b>It states facts, never a direction.</b> A dose row says one number became
/// another on a date. Nothing here decides whether that was an increase worth noting,
/// and nothing built on it may say so either.</para>
///
/// <para><b>It also READS.</b> <see cref="DoseAsOf"/> is the other half of the reason
/// this table exists: a surface describing a dose in the PAST must resolve it from the
/// ledger, never from the medication row as it stands now. Raising ProZinc from 2 IU to
/// 3 IU today would otherwise restate the 19 August entry as "3 IU · Given": a false
/// statement about a dose that was given at 2 IU, in a medical record.</para>
/// </summary>
public static class MedicationLedger
{
    /// <summary>Renders a schedule set the way the medication list does.</summary>
    public delegate string ScheduleFormatter(
        IReadOnlyCollection<DayOfWeek> days, IReadOnlyCollection<TimeSpan> times);

    /// <summary>Between the before and the after of a change. A CONSTANT rather than a
    /// literal at each site, because <see cref="DoseAsOf"/> reads the summary back apart
    /// on it: the writer and the reader cannot be allowed to drift.</summary>
    private const string Arrow = " → ";

    /// <summary>Between a started medication's dose and its schedule, same reason.</summary>
    private const string Detail = " · ";

    /// <summary>
    /// The rows one save should append. Empty when nothing the ledger tracks moved,
    /// an owner who opens the edit form and saves it untouched must not leave a mark.
    /// </summary>
    /// <param name="before">The medication as stored, or null for a create.</param>
    /// <param name="beforeSchedules">Its schedule rows as stored (ignored on a create).</param>
    /// <param name="after">The medication about to be written.</param>
    /// <param name="afterSchedules">The complete schedule set about to be written.</param>
    /// <param name="changedAtUtc">One instant shared by every row this save produces.</param>
    /// <param name="describeSchedule">How a schedule set reads as text.</param>
    public static List<MedicationChange> Diff(
        Medication? before,
        IReadOnlyList<MedicationSchedule> beforeSchedules,
        Medication after,
        IReadOnlyList<MedicationSchedule> afterSchedules,
        DateTime changedAtUtc,
        ScheduleFormatter describeSchedule)
    {
        var rows = new List<MedicationChange>();

        if (before == null)
        {
            // A create is one row, and its summary is what the medication was started
            // ON: the dose, plus the schedule when there is one. Everything a reader
            // needs to know what this treatment was at its first moment.
            var started = describeSchedule(DistinctDays(afterSchedules), DistinctTimes(afterSchedules));
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.Started,
                string.IsNullOrEmpty(started) ? Dose(after) : $"{Dose(after)}{Detail}{started}"));
            return rows;
        }

        // Fixed order, one instant: a save that renames AND re-doses reads the same way
        // every time it happens, rather than in whatever order the fields were compared.
        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.Renamed,
                $"{before.Name}{Arrow}{after.Name}"));

        // Unit alone counts: 30 mg and 30 ml are not the same treatment.
        if (before.Dosage != after.Dosage || !string.Equals(before.Unit, after.Unit, StringComparison.Ordinal))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.DoseChanged,
                $"{Dose(before)}{Arrow}{Dose(after)}"));

        // Both dimensions, because a medication is stored as one row per (day × time):
        // dropping Wednesday and adding an evening dose are both schedule changes, and
        // comparing the times alone would miss the first.
        var oldDays = DistinctDays(beforeSchedules);
        var oldTimes = DistinctTimes(beforeSchedules);
        var newDays = DistinctDays(afterSchedules);
        var newTimes = DistinctTimes(afterSchedules);
        if (!oldDays.SequenceEqual(newDays) || !oldTimes.SequenceEqual(newTimes))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.ScheduleChanged,
                $"{describeSchedule(oldDays, oldTimes)}{Arrow}{describeSchedule(newDays, newTimes)}"));

        // Retiring and un-retiring are the whole fact; there is no value to state.
        if (before.IsArchived != after.IsArchived)
            rows.Add(Row(after, changedAtUtc,
                after.IsArchived ? MedicationChangeKind.Archived : MedicationChangeKind.Restored,
                string.Empty));

        return rows;
    }

    /// <summary>
    /// What one medication read at a moment in the past, per the ledger: <c>"2 IU"</c>.
    /// Null when the ledger has nothing to say, and the caller then falls back to the
    /// medication row: pre-ledger history, where we genuinely do not know.
    ///
    /// <para><b>Started counts as well as DoseChanged.</b> A Started row's summary IS the
    /// dose the treatment began on, so including it answers correctly for every entry
    /// between the first prescription and the first change. Without it the whole opening
    /// stretch of a medication's life would fall through to the current row: the exact
    /// failure this method exists to end.</para>
    ///
    /// <para>The summary is stored TEXT and is read back apart on <see cref="Arrow"/> and
    /// <see cref="Detail"/>, which are constants shared with the writer above. That is the
    /// price of a self-contained row, and it is the right price: the alternative is
    /// re-rendering the past from the present, which is the bug.</para>
    ///
    /// <para>A row whose <c>MedicationId</c> is 0 (pulled from the cloud before its
    /// medication arrived) simply does not participate. It cannot be attributed, and
    /// guessing by name would attribute it to whatever the medication is called
    /// <i>now</i>.</para>
    /// </summary>
    /// <param name="changes">The pet's ledger. Any order; scoped by pet, not medication.</param>
    /// <param name="medicationId">Which medication's dose is being asked about.</param>
    /// <param name="asOfUtc">The moment. Rows stamped after it are not yet true.</param>
    public static string? DoseAsOf(
        IEnumerable<MedicationChange> changes, int medicationId, DateTime asOfUtc)
    {
        if (medicationId == 0)
            return null;

        MedicationChange? latest = null;
        foreach (var change in changes)
        {
            if (change.MedicationId != medicationId
                || change.IsDeleted
                || change.ChangedAtUtc > asOfUtc
                || !StatesADose(change.Kind))
                continue;

            // Ties broken by insert order: one save appends its rows at one instant, and
            // the later row is the later fact.
            if (latest is null
                || change.ChangedAtUtc > latest.ChangedAtUtc
                || (change.ChangedAtUtc == latest.ChangedAtUtc && change.Id > latest.Id))
                latest = change;
        }

        return latest is null ? null : DoseTextOf(latest);
    }

    /// <summary>
    /// What the dose read over a PERIOD: <c>"30 mg"</c>, or <c>"30 mg → 45 mg"</c> when
    /// it moved inside it. Empty when the ledger has nothing to say, and the caller then
    /// falls back to the medication row.
    ///
    /// <para>A period is a stretch, not a moment, so one number cannot always state it. A
    /// report for March generated in August used to print August's dose against March's
    /// counts; naming both ends when they differ is what the period actually WAS.</para>
    ///
    /// <para>This is not the banned before/after: no count sits either side of it,
    /// nothing here compares the two numbers, and nothing says which way they went. It is
    /// the same sentence the ledger itself prints.</para>
    /// </summary>
    /// <param name="fromUtc">Start of the period. The dose in force at this instant.</param>
    /// <param name="toUtc">End of the period, exclusive-ish: pass the instant after the
    /// last day so a change made on that day counts as inside.</param>
    public static string DoseOverPeriod(
        IEnumerable<MedicationChange> changes, int medicationId, DateTime fromUtc, DateTime toUtc)
    {
        var rows = changes as IReadOnlyCollection<MedicationChange> ?? changes.ToList();

        var atEnd = DoseAsOf(rows, medicationId, toUtc);
        if (atEnd is null)
            return string.Empty;

        var atStart = DoseAsOf(rows, medicationId, fromUtc);

        // Null at the start means the medication began inside the period; there is no
        // earlier reading to name and inventing one would be a claim.
        return atStart is null || string.Equals(atStart, atEnd, StringComparison.Ordinal)
            ? atEnd
            : atStart + Arrow + atEnd;
    }

    private static bool StatesADose(MedicationChangeKind kind) =>
        kind is MedicationChangeKind.Started or MedicationChangeKind.DoseChanged;

    /// <summary>The dose a row leaves in force: the right side of a change's arrow, or a
    /// started row's summary up to its schedule.</summary>
    private static string? DoseTextOf(MedicationChange change)
    {
        var summary = change.Summary;
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        if (change.Kind == MedicationChangeKind.DoseChanged)
        {
            var at = summary.LastIndexOf(Arrow, StringComparison.Ordinal);
            return at < 0 ? null : Trimmed(summary[(at + Arrow.Length)..]);
        }

        var detail = summary.IndexOf(Detail, StringComparison.Ordinal);
        return Trimmed(detail < 0 ? summary : summary[..detail]);
    }

    private static string? Trimmed(string value)
    {
        value = value.Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>The row a deletion leaves behind. Not a diff: there is no "after" to
    /// compare against, and the point of the row is that the medication is gone while
    /// the fact that it was ever given is not.</summary>
    public static MedicationChange Stopped(Medication medication, DateTime changedAtUtc)
        => Row(medication, changedAtUtc, MedicationChangeKind.Stopped, string.Empty);

    /// <summary>The dose as the medication list writes it. Current culture, matching
    /// <c>FilteredMedication.DoseDisplay</c>: the number an owner is shown is the
    /// number their ledger should quote back at them.</summary>
    private static string Dose(Medication m)
        => string.Format(CultureInfo.CurrentCulture, "{0} {1}", m.Dosage, m.Unit);

    private static List<DayOfWeek> DistinctDays(IReadOnlyList<MedicationSchedule> rows)
        => rows.Select(s => s.Day).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();

    private static List<TimeSpan> DistinctTimes(IReadOnlyList<MedicationSchedule> rows)
        => rows.Select(s => s.Time).Distinct().OrderBy(t => t).ToList();

    /// <summary>
    /// The name stamped on every row of one save is the name the medication carries
    /// AFTER it: "what this treatment is called from here on". A rename row keeps the
    /// old name inside its summary, so nothing is lost, and a reader scanning the
    /// ledger for a medication finds all of its rows under one heading instead of
    /// having the history split at the moment it was renamed.
    /// </summary>
    private static MedicationChange Row(
        Medication medication, DateTime changedAtUtc, MedicationChangeKind kind, string summary)
        => new()
        {
            PetId = medication.PetId,
            MedicationId = medication.Id,
            ChangedAtUtc = changedAtUtc,
            Kind = kind,
            MedicationName = medication.Name,
            Summary = summary,
        };
}
