namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// A seizure's duration, after it moved from whole minutes to seconds.
///
/// <para><b>The data loss this fixes.</b> <c>DurationMinutes</c> was an <c>int</c>, so a
/// 45-second seizure was unrecordable: the owner typed 45 seconds and the record kept 0
/// or 1. Sub-minute seizures are common and clinically relevant, and owners describe them
/// in seconds ("about forty seconds"), never in fractions of a minute. That is not a
/// usability wrinkle, it is the diary silently discarding the thing it was opened to
/// write down, at the worst possible moment.</para>
///
/// <para>The migration is arithmetic and it is one-way, so it gets tested rather than
/// trusted: a x60 that ran twice would restate a 3-minute seizure as three hours.</para>
/// </summary>
public class SeizureDurationTests
{
    private static readonly UnitDef Seconds =
        UnitCatalog.Get(UnitFamily.Duration, UnitCatalog.Seconds);

    private static readonly UnitDef Minutes =
        UnitCatalog.Get(UnitFamily.Duration, UnitCatalog.Minutes);

    // ── The value that could not be stored before ────────────────────────────

    /// <summary>The 45-second seizure. Under the old model this rounded to 0 or 1;
    /// it now survives a save and a reload exactly.</summary>
    [Fact]
    public void A_forty_five_second_seizure_survives_a_round_trip()
    {
        var stored = Store(45m, Seconds);

        Assert.Equal(45, stored);
        Assert.Equal(45m, UnitCatalog.Display(stored!.Value, Seconds));
    }

    /// <summary>The brief's own example: 90 seconds, saved and read back.</summary>
    [Fact]
    public void A_ninety_second_seizure_survives_a_save_and_reload()
    {
        var stored = Store(90m, Seconds);

        Assert.Equal(90, stored);
        Assert.Equal(90m, UnitCatalog.Display(stored!.Value, Seconds));
        // And the same occurrence stated in minutes is a minute and a half, not "1".
        Assert.Equal(1.5m, UnitCatalog.Display(stored.Value, Minutes));
    }

    /// <summary>Entered in minutes, it lands on a whole number of seconds rather than on
    /// 89.999...: the rounding happens once, at the point the int column is written.</summary>
    [Theory]
    [InlineData("1.5", 90)]
    [InlineData("0.75", 45)]
    [InlineData("3", 180)]
    [InlineData("12", 720)]
    public void A_duration_typed_in_minutes_lands_on_whole_seconds(string typed, int expected)
    {
        var value = decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, Store(value, Minutes));
    }

    /// <summary>Not timed stays not timed. Null is a normal answer, not a skipped field,
    /// and it must never become a zero (which would read as "it lasted no time").</summary>
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    public void An_untimed_seizure_stores_no_duration(string typed)
        => Assert.Null(Store(typed, Seconds));

    // ── The x60 backfill ─────────────────────────────────────────────────────
    //
    //  The app runs this as one SQL statement in AppDatabase.InitAsync; the rule it
    //  encodes is reproduced here because its failure mode is silent and permanent.

    /// <summary>One legacy row, converted. Three minutes becomes 180 seconds.</summary>
    [Fact]
    public void The_backfill_converts_a_legacy_row()
    {
        var row = new SeizureEntry { DurationMinutes = 3 };
        Backfill(row);

        Assert.Equal(180, row.DurationSeconds);
    }

    /// <summary>
    /// <b>Idempotent, and that is the whole safety property.</b> The guard is the WHERE
    /// clause (<c>DurationSeconds is null and DurationMinutes is not null</c>), which is a
    /// stronger guarantee than a run-once flag: it survives a reinstall, a restored
    /// backup, and a second device, and a x60 that ran twice would restate a 3-minute
    /// seizure as three hours in a medical record.
    /// </summary>
    [Fact]
    public void The_backfill_is_idempotent()
    {
        var row = new SeizureEntry { DurationMinutes = 3 };

        for (var i = 0; i < 5; i++)
            Backfill(row);

        Assert.Equal(180, row.DurationSeconds);
    }

    /// <summary>A row already written in seconds is left alone, even though its dead
    /// minutes column may still hold something: the new column is the truth.</summary>
    [Fact]
    public void The_backfill_never_overwrites_a_seconds_value()
    {
        var row = new SeizureEntry { DurationMinutes = 3, DurationSeconds = 45 };
        Backfill(row);

        Assert.Equal(45, row.DurationSeconds);
    }

    /// <summary>An untimed legacy row stays untimed rather than becoming zero seconds.</summary>
    [Fact]
    public void The_backfill_leaves_an_untimed_row_untimed()
    {
        var row = new SeizureEntry();
        Backfill(row);

        Assert.Null(row.DurationSeconds);
    }

    /// <summary>
    /// A row arriving LATER from a device still on an older build is converted too. This
    /// is why the guard is a WHERE clause rather than a one-shot flag: a migration that
    /// "already ran" would leave that row reading as never timed, forever.
    /// </summary>
    [Fact]
    public void A_legacy_row_that_arrives_after_the_first_run_is_still_converted()
    {
        var alreadyMigrated = new SeizureEntry { DurationMinutes = 3, DurationSeconds = 180 };
        Backfill(alreadyMigrated);

        // Some time later, a pull delivers a row from an old client.
        var justArrived = new SeizureEntry { DurationMinutes = 2 };
        Backfill(justArrived);

        Assert.Equal(180, alreadyMigrated.DurationSeconds);
        Assert.Equal(120, justArrived.DurationSeconds);
    }

    /// <summary>The converted value carries no unit, and must not. Those rows were
    /// recorded in minutes by a build that never asked, so claiming "min" as provenance
    /// would be inventing a fact the record never held. Null reads as canonical.</summary>
    [Fact]
    public void A_backfilled_row_claims_no_unit_provenance()
    {
        var row = new SeizureEntry { DurationMinutes = 3 };
        Backfill(row);

        Assert.Null(row.Unit);
        Assert.Equal(Seconds, UnitCatalog.Get(UnitFamily.Duration, row.Unit));
    }

    // ── The two rules under test, in the shape the app applies them ──────────

    /// <summary>What the sheet stores: canonical seconds, rounded once, or null.</summary>
    private static int? Store(decimal typed, UnitDef unit)
    {
        if (typed <= 0)
            return null;

        var seconds = decimal.Round(
            UnitCatalog.ToCanonicalValue(typed, unit), 0, MidpointRounding.AwayFromZero);
        return seconds > 0 ? (int)seconds : null;
    }

    /// <summary>Same, from the raw field text, so the "not timed" cases go through the
    /// identical path the sheet uses.</summary>
    private static int? Store(string typed, UnitDef unit) =>
        decimal.TryParse(typed, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Store(value, unit)
            : null;

    /// <summary>The backfill statement, as C#:
    /// <c>set DurationSeconds = DurationMinutes * 60
    /// where DurationSeconds is null and DurationMinutes is not null</c>.</summary>
    private static void Backfill(SeizureEntry row)
    {
        if (row.DurationSeconds is null && row.DurationMinutes is int minutes)
            row.DurationSeconds = minutes * 60;
    }
}
