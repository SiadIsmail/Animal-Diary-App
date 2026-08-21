namespace Animal_Diary_App.Data.Models;

using System.Globalization;
using Animal_Diary_App.Helpers;

/// <summary>
/// How one treatment-ledger row reads. <b>One renderer</b>, for the same reason
/// <see cref="RecordFactsText"/> is one: the appointment summary's "what changed" list
/// and the markers on Today's charts describe the same rows, and a row that reads
/// differently on one surface is a row the app chose to phrase differently there.
///
/// <para>Everything here comes from the STORED row: the name and the summary were
/// rendered at the moment of the change and written down as text, so a medication since
/// renamed, retired or deleted still reads correctly. Nothing is re-derived from the
/// medication as it stands now; doing that is the bug the ledger exists to end
/// (AI/domain.md).</para>
///
/// <para><b>Chronological facts only.</b> A row says one number became another on a
/// date. Nothing here decides whether that was an increase worth noting, and nothing
/// built on it may put two counts either side of one.</para>
/// </summary>
public static class LedgerText
{
    private static LocalizationManager Loc => LocalizationManager.Instance;

    /// <summary>
    /// The fact itself: <c>"30 mg → 45 mg"</c>, or the kind's own wording where there is
    /// no value to state (started, archived, restored, stopped).
    /// </summary>
    public static string Fact(MedicationChange change)
    {
        if (!string.IsNullOrWhiteSpace(change.Summary))
            return change.Kind == MedicationChangeKind.Started
                ? Loc.Format("Vet_LedgerStarted", change.Summary)
                : change.Summary;

        return Loc.GetString(change.Kind switch
        {
            MedicationChangeKind.Archived => "Vet_LedgerArchived",
            MedicationChangeKind.Restored => "Vet_LedgerRestored",
            MedicationChangeKind.Stopped => "Vet_LedgerStopped",
            _ => "Vet_LedgerChanged",
        });
    }

    /// <summary>
    /// The whole line: <c>"22 May · Phenobarbital 30 mg → 45 mg"</c>: the date, the
    /// medication as it was called then, and what changed.
    ///
    /// <para>This is everything a marker's label may say. If a label would need to say
    /// anything beyond what changed and when, it has gone too far.</para>
    /// </summary>
    public static string Line(MedicationChange change) =>
        $"{Day(change)} · {change.MedicationName} {Fact(change)}".Trim();

    /// <summary>The ledger is stamped in UTC; the owner reads it in their own time.</summary>
    public static DateTime LocalDate(MedicationChange change) =>
        change.ChangedAtUtc.ToLocalTime().Date;

    private static string Day(MedicationChange change) =>
        LocalDate(change).ToString("d MMM", CultureInfo.CurrentCulture);
}
