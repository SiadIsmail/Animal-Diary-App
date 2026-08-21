namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// How deep the record is, as a coarse bucket, for the one property the paid boundary
/// stands or falls on.
///
/// <para><b>Why this exists.</b> The whole strategy rests on a single falsifiable claim:
/// <i>willingness to pay rises with accumulated history.</i> Segmenting conversion by this
/// is the only thing that can test it. If month-4+ owners convert at several times the
/// rate of month-1 owners, the flywheel is real and the later move is to raise the price
/// rather than lower it. If the curve is flat, the paid tier is not compounding and the
/// boundary needs rethinking rather than repricing.</para>
///
/// <para><b>Why its own buckets rather than <see cref="AnalyticsTenure"/>'s.</b> That one
/// answers "is this install new", and tops out at 15+ days — every question here would
/// land in its last bucket. Chronic care runs on a three-to-six month appointment rhythm,
/// so the boundaries that matter are months, not days.</para>
///
/// <para><b>Bucketed, never exact</b> — the same rule and the same reason as
/// <see cref="AnalyticsTenure"/>. A precise history length alongside a timestamp is a far
/// narrower fingerprint than the question needs, and it says nothing about the animal:
/// this is a number describing the <i>event</i>, not the user. It carries no pet, no
/// condition, no medical detail and no count of anything logged.</para>
///
/// <para>Pure and MAUI-free, so the boundaries are unit-testable — an off-by-one here is
/// invisible and would quietly move a cohort into the wrong column for months.</para>
/// </summary>
public static class AnalyticsHistory
{
    /// <summary>Nothing has been written down for this pet yet.</summary>
    public const string None = "0";
    /// <summary>The first week.</summary>
    public const string FirstWeek = "1-7";
    /// <summary>The first month.</summary>
    public const string FirstMonth = "8-30";
    /// <summary>Months two and three — inside the first appointment cycle.</summary>
    public const string Months2To3 = "31-90";
    /// <summary>Months four to six — the first owners to have crossed a full cycle
    /// WITH a record behind them. The cohort the whole claim is about.</summary>
    public const string Months4To6 = "91-180";
    /// <summary>Past six months.</summary>
    public const string Beyond = "181+";

    /// <summary>Days between the pet's first entry and today, as a bucket. A negative
    /// span (a device clock moved backwards) reads as <see cref="None"/> rather than
    /// throwing — this is telemetry, and it may never be the thing that breaks a sheet.</summary>
    public static string Bucket(int days) => days switch
    {
        <= 0 => None,
        <= 7 => FirstWeek,
        <= 30 => FirstMonth,
        <= 90 => Months2To3,
        <= 180 => Months4To6,
        _ => Beyond,
    };

    /// <summary>Whole days from the pet's first entry to today, floored at zero.</summary>
    public static int DaysBetween(DateTime firstEntry, DateTime today) =>
        Math.Max(0, (int)(today.Date - firstEntry.Date).TotalDays);
}
