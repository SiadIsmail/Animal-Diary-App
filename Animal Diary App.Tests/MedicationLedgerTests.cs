namespace Animal_Diary_App.Tests;

using System.Globalization;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Xunit;

/// <summary>
/// The treatment ledger's diff. Every failure this covers is silent on a device —
/// nothing crashes, the history is simply wrong from then on, and it cannot be
/// reconstructed afterwards. That is the whole reason the diff was kept pure.
/// </summary>
public class MedicationLedgerTests
{
    private static readonly DateTime When = new(2026, 5, 22, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>Stands in for the localized formatter the app passes. Deterministic and
    /// resource-free, so these tests never depend on which language is loaded.</summary>
    private static string Describe(
        IReadOnlyCollection<DayOfWeek> days, IReadOnlyCollection<TimeSpan> times)
    {
        var clock = string.Join(" · ", times.Select(t => t.ToString(@"hh\:mm")));
        if (days.Count == 0 || days.Count >= 7)
            return clock;
        var names = string.Join(", ", days.Select(d => d.ToString()[..3]));
        return string.IsNullOrEmpty(clock) ? names : $"{names} · {clock}";
    }

    private static Medication Med(
        string name = "Phenobarbital", decimal dosage = 30m, string unit = "mg",
        bool archived = false, int id = 7, int petId = 3) => new()
        {
            Id = id,
            PetId = petId,
            Name = name,
            Dosage = dosage,
            Unit = unit,
            IsArchived = archived,
        };

    private static List<MedicationSchedule> Schedule(params (DayOfWeek Day, int Hour)[] slots)
        => slots.Select(s => new MedicationSchedule
        {
            MedicationId = 7,
            Day = s.Day,
            Time = TimeSpan.FromHours(s.Hour),
        }).ToList();

    private static List<MedicationChange> Diff(
        Medication? before, IReadOnlyList<MedicationSchedule> beforeSchedules,
        Medication after, IReadOnlyList<MedicationSchedule> afterSchedules)
        => MedicationLedger.Diff(before, beforeSchedules, after, afterSchedules, When, Describe);

    // ── The change that was being destroyed ──────────────────────────────────

    [Fact]
    public void DoseEdit_WritesExactlyOneRow_WithBothNumbers()
    {
        var before = Med(dosage: 30m);
        var after = Med(dosage: 45m);
        var schedules = Schedule((DayOfWeek.Monday, 8));

        var rows = Diff(before, schedules, after, schedules);

        var row = Assert.Single(rows);
        Assert.Equal(MedicationChangeKind.DoseChanged, row.Kind);
        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, "{0} mg → {1} mg", 30m, 45m),
            row.Summary);
        Assert.Equal(When, row.ChangedAtUtc);
    }

    /// <summary>A unit change with the same number is still a different treatment —
    /// 30 mg and 30 ml are not the same thing given twice.</summary>
    [Fact]
    public void UnitChangeAlone_IsADoseChange()
    {
        var rows = Diff(Med(unit: "mg"), Schedule(), Med(unit: "ml"), Schedule());

        var row = Assert.Single(rows);
        Assert.Equal(MedicationChangeKind.DoseChanged, row.Kind);
    }

    // ── The half that matters more: what must NOT be recorded ────────────────

    [Fact]
    public void UnchangedSave_WritesNothing()
    {
        var schedules = Schedule((DayOfWeek.Monday, 8), (DayOfWeek.Thursday, 20));

        // A new list of equal rows, as the save path always produces — the schedule set
        // is replaced wholesale on every save, so comparing row identity rather than
        // (day, time) would mark every single save as a schedule change.
        var rows = Diff(Med(), schedules, Med(), Schedule((DayOfWeek.Monday, 8), (DayOfWeek.Thursday, 20)));

        Assert.Empty(rows);
    }

    /// <summary>The editor writes one row per (day × time), so the same set can arrive in
    /// any order. Order is not a change.</summary>
    [Fact]
    public void ReorderedScheduleRows_AreNotAChange()
    {
        var before = Schedule((DayOfWeek.Thursday, 20), (DayOfWeek.Monday, 8));
        var after = Schedule((DayOfWeek.Monday, 8), (DayOfWeek.Thursday, 20));

        Assert.Empty(Diff(Med(), before, Med(), after));
    }

    // ── Several things at once ───────────────────────────────────────────────

    [Fact]
    public void RenamePlusDoseChange_WritesTwoRows_InFixedOrder()
    {
        var before = Med(name: "Phenobarb", dosage: 30m);
        var after = Med(name: "Phenobarbital", dosage: 45m);

        var rows = Diff(before, Schedule(), after, Schedule());

        Assert.Equal(2, rows.Count);
        Assert.Equal(MedicationChangeKind.Renamed, rows[0].Kind);
        Assert.Equal("Phenobarb → Phenobarbital", rows[0].Summary);
        Assert.Equal(MedicationChangeKind.DoseChanged, rows[1].Kind);

        // One save is one moment, however many facts it produced.
        Assert.All(rows, r => Assert.Equal(When, r.ChangedAtUtc));
    }

    /// <summary>Dropping a day is a schedule change even when the times are identical —
    /// comparing the times alone missed it, which is the bug that made every weekly
    /// medication read as "once daily" in the list this formatter is shared with.</summary>
    [Fact]
    public void DroppingADay_IsAScheduleChange()
    {
        var before = Schedule((DayOfWeek.Monday, 8), (DayOfWeek.Wednesday, 8));
        var after = Schedule((DayOfWeek.Monday, 8));

        var row = Assert.Single(Diff(Med(), before, Med(), after));
        Assert.Equal(MedicationChangeKind.ScheduleChanged, row.Kind);
        Assert.Equal("Mon, Wed · 08:00 → Mon · 08:00", row.Summary);
    }

    // ── Creates, archiving, stopping ─────────────────────────────────────────

    [Fact]
    public void Create_WritesOneStartedRow_CarryingDoseAndSchedule()
    {
        var rows = Diff(null, Array.Empty<MedicationSchedule>(),
            Med(dosage: 250m), Schedule((DayOfWeek.Monday, 8), (DayOfWeek.Monday, 20)));

        var row = Assert.Single(rows);
        Assert.Equal(MedicationChangeKind.Started, row.Kind);
        Assert.Contains("250 mg", row.Summary);
        Assert.Contains("08:00", row.Summary);
    }

    [Fact]
    public void ArchiveAndRestore_AreTheirOwnKinds_AndStateNoValue()
    {
        var archived = Assert.Single(Diff(Med(archived: false), Schedule(), Med(archived: true), Schedule()));
        Assert.Equal(MedicationChangeKind.Archived, archived.Kind);
        Assert.Equal(string.Empty, archived.Summary);

        var restored = Assert.Single(Diff(Med(archived: true), Schedule(), Med(archived: false), Schedule()));
        Assert.Equal(MedicationChangeKind.Restored, restored.Kind);
        Assert.Equal(string.Empty, restored.Summary);
    }

    /// <summary>The point of the ledger: the row outlives the medication, so it must
    /// carry everything a reader needs with nothing left to join to.</summary>
    [Fact]
    public void StoppedRow_IsSelfContained()
    {
        var row = MedicationLedger.Stopped(Med(name: "Levetiracetam", petId: 3), When);

        Assert.Equal(MedicationChangeKind.Stopped, row.Kind);
        Assert.Equal("Levetiracetam", row.MedicationName);
        Assert.Equal(3, row.PetId);
    }

    /// <summary>Every row a save produces is filed under the pet and under the name the
    /// medication carries afterwards, so a rename does not split its own history in two.
    /// The old name is not lost — the rename row's summary holds it.</summary>
    [Fact]
    public void EveryRow_CarriesThePetAndTheNameAsOfTheChange()
    {
        var rows = Diff(Med(name: "Phenobarb", dosage: 30m), Schedule(),
            Med(name: "Phenobarbital", dosage: 45m), Schedule());

        Assert.All(rows, r =>
        {
            Assert.Equal(3, r.PetId);
            Assert.Equal("Phenobarbital", r.MedicationName);
        });
        Assert.Contains("Phenobarb →", rows[0].Summary);
    }
}
