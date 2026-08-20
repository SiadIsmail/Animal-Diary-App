namespace Animal_Diary_App.Data.Services;

using System.Globalization;
using Animal_Diary_App.Data.Models;

/// <summary>
/// What changed between two versions of a medication, as ledger rows.
///
/// <para><b>Pure by design.</b> It takes the before and after and hands back rows;
/// it opens nothing and writes nothing. That is what lets the rules below —
/// "an unchanged save writes NO row", "a rename plus a dose change writes two" — be
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
/// </summary>
public static class MedicationLedger
{
    /// <summary>Renders a schedule set the way the medication list does.</summary>
    public delegate string ScheduleFormatter(
        IReadOnlyCollection<DayOfWeek> days, IReadOnlyCollection<TimeSpan> times);

    /// <summary>
    /// The rows one save should append. Empty when nothing the ledger tracks moved —
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
            // ON — the dose, plus the schedule when there is one. Everything a reader
            // needs to know what this treatment was at its first moment.
            var started = describeSchedule(DistinctDays(afterSchedules), DistinctTimes(afterSchedules));
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.Started,
                string.IsNullOrEmpty(started) ? Dose(after) : $"{Dose(after)} · {started}"));
            return rows;
        }

        // Fixed order, one instant: a save that renames AND re-doses reads the same way
        // every time it happens, rather than in whatever order the fields were compared.
        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.Renamed,
                $"{before.Name} → {after.Name}"));

        // Unit alone counts: 30 mg and 30 ml are not the same treatment.
        if (before.Dosage != after.Dosage || !string.Equals(before.Unit, after.Unit, StringComparison.Ordinal))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.DoseChanged,
                $"{Dose(before)} → {Dose(after)}"));

        // Both dimensions, because a medication is stored as one row per (day × time):
        // dropping Wednesday and adding an evening dose are both schedule changes, and
        // comparing the times alone would miss the first.
        var oldDays = DistinctDays(beforeSchedules);
        var oldTimes = DistinctTimes(beforeSchedules);
        var newDays = DistinctDays(afterSchedules);
        var newTimes = DistinctTimes(afterSchedules);
        if (!oldDays.SequenceEqual(newDays) || !oldTimes.SequenceEqual(newTimes))
            rows.Add(Row(after, changedAtUtc, MedicationChangeKind.ScheduleChanged,
                $"{describeSchedule(oldDays, oldTimes)} → {describeSchedule(newDays, newTimes)}"));

        // Retiring and un-retiring are the whole fact; there is no value to state.
        if (before.IsArchived != after.IsArchived)
            rows.Add(Row(after, changedAtUtc,
                after.IsArchived ? MedicationChangeKind.Archived : MedicationChangeKind.Restored,
                string.Empty));

        return rows;
    }

    /// <summary>The row a deletion leaves behind. Not a diff — there is no "after" to
    /// compare against, and the point of the row is that the medication is gone while
    /// the fact that it was ever given is not.</summary>
    public static MedicationChange Stopped(Medication medication, DateTime changedAtUtc)
        => Row(medication, changedAtUtc, MedicationChangeKind.Stopped, string.Empty);

    /// <summary>The dose as the medication list writes it. Current culture, matching
    /// <c>FilteredMedication.DoseDisplay</c> — the number an owner is shown is the
    /// number their ledger should quote back at them.</summary>
    private static string Dose(Medication m)
        => string.Format(CultureInfo.CurrentCulture, "{0} {1}", m.Dosage, m.Unit);

    private static List<DayOfWeek> DistinctDays(IReadOnlyList<MedicationSchedule> rows)
        => rows.Select(s => s.Day).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();

    private static List<TimeSpan> DistinctTimes(IReadOnlyList<MedicationSchedule> rows)
        => rows.Select(s => s.Time).Distinct().OrderBy(t => t).ToList();

    /// <summary>
    /// The name stamped on every row of one save is the name the medication carries
    /// AFTER it — "what this treatment is called from here on". A rename row keeps the
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
