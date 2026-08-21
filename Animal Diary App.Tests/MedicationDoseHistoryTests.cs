namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Xunit;

/// <summary>
/// Reading a PAST dose out of the treatment ledger.
///
/// <para><b>The bug these exist for.</b> The Journal timeline and the vet report both
/// described a dose by reading <c>Medication.Dosage</c>: the row as it stands NOW. Raise
/// ProZinc from 2 IU to 3 IU today and the 19 August entry silently restated itself as
/// "3 IU · Given", which is a false statement about a dose that was given at 2 IU, sitting
/// in a medical record, produced by an edit made on a different screen. Nothing crashed
/// and nothing looked wrong; the history was simply rewritten.</para>
///
/// <para>Both surfaces now go through <see cref="MedicationLedger.DoseAsOf"/> and
/// <see cref="MedicationLedger.DoseOverPeriod"/>, which is why they are tested here and
/// not through a page: the timeline is a MAUI ViewModel and the report needs SQLite, but
/// the DECISION both of them make is this arithmetic, and it is pure.</para>
/// </summary>
public class MedicationDoseHistoryTests
{
    private const int MedId = 7;
    private const int OtherMedId = 8;

    private static DateTime Utc(int day, int hour = 9) =>
        new(2026, 5, day, hour, 0, 0, DateTimeKind.Utc);

    private static MedicationChange Row(
        MedicationChangeKind kind, string summary, DateTime at,
        int medicationId = MedId, int id = 0) => new()
        {
            Id = id,
            PetId = 3,
            MedicationId = medicationId,
            Kind = kind,
            ChangedAtUtc = at,
            MedicationName = "ProZinc",
            Summary = summary,
        };

    /// <summary>Started on 2 IU on the 1st, raised to 3 IU on the 20th.</summary>
    private static List<MedicationChange> Ledger() => new()
    {
        Row(MedicationChangeKind.Started, "2 IU · Mon, Tue 08:00", Utc(1), id: 1),
        Row(MedicationChangeKind.DoseChanged, "2 IU → 3 IU", Utc(20), id: 2),
    };

    // ── The timeline ─────────────────────────────────────────────────────────

    [Fact]
    public void A_dose_logged_before_the_change_still_reads_the_old_value()
    {
        Assert.Equal("2 IU", MedicationLedger.DoseAsOf(Ledger(), MedId, Utc(19, 20)));
    }

    [Fact]
    public void A_dose_logged_after_the_change_reads_the_new_value()
    {
        Assert.Equal("3 IU", MedicationLedger.DoseAsOf(Ledger(), MedId, Utc(21)));
    }

    /// <summary>The change lands at 09:00; the 08:00 dose beneath it was given at the old
    /// value. Resolving by DAY rather than by moment would rewrite it, which is the same
    /// class of error the whole fix exists to remove, one day narrower.</summary>
    [Fact]
    public void A_dose_earlier_on_the_day_of_the_change_reads_the_old_value()
    {
        Assert.Equal("2 IU", MedicationLedger.DoseAsOf(Ledger(), MedId, Utc(20, 8)));
        Assert.Equal("3 IU", MedicationLedger.DoseAsOf(Ledger(), MedId, Utc(20, 10)));
    }

    /// <summary>Between the first prescription and the first change there is no
    /// DoseChanged row at all: a Started row's summary IS the dose it began on, and
    /// without it that whole opening stretch would fall through to the current value.</summary>
    [Fact]
    public void The_started_row_answers_for_the_stretch_before_any_change()
    {
        var onlyStarted = new List<MedicationChange>
        {
            Row(MedicationChangeKind.Started, "2 IU · Mon, Tue 08:00", Utc(1), id: 1),
        };

        Assert.Equal("2 IU", MedicationLedger.DoseAsOf(onlyStarted, MedId, Utc(15)));
    }

    /// <summary>A started row with no schedule carries the bare dose.</summary>
    [Fact]
    public void A_started_row_without_a_schedule_still_yields_its_dose()
    {
        var rows = new List<MedicationChange>
        {
            Row(MedicationChangeKind.Started, "30 mg", Utc(1), id: 1),
        };

        Assert.Equal("30 mg", MedicationLedger.DoseAsOf(rows, MedId, Utc(15)));
    }

    // ── The fallback ─────────────────────────────────────────────────────────

    /// <summary>A pet with no ledger rows at all: every install that predates the
    /// ledger. Null, not an exception and not a guess: the caller falls back to the
    /// medication row, which is honest, because we genuinely do not know.</summary>
    [Fact]
    public void No_ledger_rows_yields_null_rather_than_throwing()
    {
        Assert.Null(MedicationLedger.DoseAsOf(
            Array.Empty<MedicationChange>(), MedId, Utc(19)));
        Assert.Equal(string.Empty, MedicationLedger.DoseOverPeriod(
            Array.Empty<MedicationChange>(), MedId, Utc(1), Utc(30)));
    }

    /// <summary>Rows exist, but all of them are AFTER the moment asked about: an entry
    /// imported from before the medication was ever recorded. Same answer.</summary>
    [Fact]
    public void A_moment_before_every_row_yields_null()
    {
        Assert.Null(MedicationLedger.DoseAsOf(Ledger(), MedId, Utc(1, 8)));
    }

    /// <summary>The ledger is scoped by PET, so it carries every medication's rows. One
    /// medication's history may never answer for another's.</summary>
    [Fact]
    public void Another_medications_rows_are_not_borrowed()
    {
        var rows = new List<MedicationChange>
        {
            Row(MedicationChangeKind.Started, "2 IU", Utc(1), medicationId: OtherMedId, id: 1),
        };

        Assert.Null(MedicationLedger.DoseAsOf(rows, MedId, Utc(19)));
    }

    /// <summary>A row pulled from the cloud before its medication carries
    /// <c>MedicationId == 0</c>. It cannot be attributed to anything, and guessing by
    /// name would attribute it to whatever the medication is called now.</summary>
    [Fact]
    public void An_unattributable_row_does_not_answer()
    {
        var rows = new List<MedicationChange>
        {
            Row(MedicationChangeKind.Started, "2 IU", Utc(1), medicationId: 0, id: 1),
        };

        Assert.Null(MedicationLedger.DoseAsOf(rows, medicationId: 0, Utc(19)));
        Assert.Null(MedicationLedger.DoseAsOf(rows, MedId, Utc(19)));
    }

    /// <summary>A soft-deleted row is not history, it is a row on its way out.</summary>
    [Fact]
    public void A_deleted_row_does_not_answer()
    {
        var rows = Ledger();
        rows[1].IsDeleted = true;

        Assert.Equal("2 IU", MedicationLedger.DoseAsOf(rows, MedId, Utc(25)));
    }

    /// <summary>Kinds that do not state a dose are ignored, including the rename row,
    /// whose summary also contains an arrow.</summary>
    [Fact]
    public void A_rename_row_is_never_read_as_a_dose()
    {
        var rows = Ledger();
        rows.Add(Row(MedicationChangeKind.Renamed, "ProZinc → Prozinc UK", Utc(25), id: 3));

        Assert.Equal("3 IU", MedicationLedger.DoseAsOf(rows, MedId, Utc(26)));
    }

    // ── The report's period ──────────────────────────────────────────────────

    /// <summary>The bug in the report, stated exactly: a period that ended before the
    /// current dose ever existed. March's report, generated in August.</summary>
    [Fact]
    public void A_period_entirely_before_the_change_states_the_old_dose()
    {
        Assert.Equal("2 IU", MedicationLedger.DoseOverPeriod(
            Ledger(), MedId, Utc(2), Utc(19)));
    }

    [Fact]
    public void A_period_entirely_after_the_change_states_the_new_dose()
    {
        Assert.Equal("3 IU", MedicationLedger.DoseOverPeriod(
            Ledger(), MedId, Utc(21), Utc(30)));
    }

    /// <summary>One number cannot state a period the dose moved inside. Both ends are
    /// named, no counts either side, no comparison, no direction word.</summary>
    [Fact]
    public void A_period_spanning_the_change_names_both_ends()
    {
        Assert.Equal("2 IU → 3 IU", MedicationLedger.DoseOverPeriod(
            Ledger(), MedId, Utc(2), Utc(30)));
    }

    /// <summary>The medication began inside the period: there is no earlier reading to
    /// name, so only the one that exists is stated.</summary>
    [Fact]
    public void A_period_starting_before_the_medication_names_only_what_is_known()
    {
        var onlyStarted = new List<MedicationChange>
        {
            Row(MedicationChangeKind.Started, "2 IU", Utc(10), id: 1),
        };

        Assert.Equal("2 IU", MedicationLedger.DoseOverPeriod(
            onlyStarted, MedId, Utc(1), Utc(30)));
    }

    /// <summary>Two rows at one instant is what a single save produces. The later row is
    /// the later fact, and insert order is the only thing that can say so.</summary>
    [Fact]
    public void Rows_at_the_same_instant_are_broken_by_insert_order()
    {
        var rows = new List<MedicationChange>
        {
            Row(MedicationChangeKind.DoseChanged, "1 IU → 2 IU", Utc(20), id: 5),
            Row(MedicationChangeKind.DoseChanged, "2 IU → 3 IU", Utc(20), id: 6),
        };

        Assert.Equal("3 IU", MedicationLedger.DoseAsOf(rows, MedId, Utc(21)));
    }
}
