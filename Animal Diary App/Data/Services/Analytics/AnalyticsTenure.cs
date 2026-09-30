namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// Turns "how long has this install existed" into the coarse <c>days_since_install</c>
/// bucket attached to every event.
///
/// <para><b>Why it exists.</b> The funnel needs a "did they come back on a later day"
/// step, and a PostHog funnel cannot express "this step must happen at least a day after
/// the previous one": it only has a <i>maximum</i> conversion window. Carrying tenure on
/// the event turns that impossible time constraint into an ordinary property filter:
/// <c>app_opened where days_since_install != 0</c>.</para>
///
/// <para><b>Why buckets, not a number.</b> The privacy rule is that properties are coarse,
/// non-identifying descriptors. An exact install age plus a timestamp is a much narrower
/// fingerprint than a six-way bucket, and the product questions ("day 1 return?", "still
/// here in week 2?") are answered by the bucket. Days are counted on <b>UTC calendar
/// dates</b>, not elapsed hours, matching the usual D1/D7 convention, so an install at
/// 23:00 UTC and a return at 01:00 UTC reads as day 1.</para>
///
/// <para>Deliberately pure (no MAUI, no clock of its own) so it is unit-testable. The
/// install instant is persisted by <see cref="AnalyticsIdentity"/>.</para>
/// </summary>
public static class AnalyticsTenure
{
    /// <summary>Same UTC day as the install: the acquisition session.</summary>
    public const string BucketDay0 = "0";
    /// <summary>The next UTC day: the classic D1 return.</summary>
    public const string BucketDay1 = "1";
    public const string BucketDays2To3 = "2-3";
    public const string BucketDays4To7 = "4-7";
    /// <summary>Second week.</summary>
    public const string BucketDays8To14 = "8-14";
    /// <summary>Two weeks in and beyond. Everything long-running lives here.</summary>
    public const string BucketDays15Plus = "15+";

    /// <summary>Whole UTC calendar days between the install and now; never negative
    /// (a backwards clock reads as day 0 rather than a nonsense bucket).</summary>
    public static int DaysSince(DateTime firstSeenUtc, DateTime nowUtc)
    {
        var days = (nowUtc.Date - firstSeenUtc.Date).Days;
        return days < 0 ? 0 : days;
    }

    /// <summary>The bucket for a day count.</summary>
    public static string Bucket(int days) => days switch
    {
        <= 0 => BucketDay0,
        1 => BucketDay1,
        <= 3 => BucketDays2To3,
        <= 7 => BucketDays4To7,
        <= 14 => BucketDays8To14,
        _ => BucketDays15Plus,
    };

    /// <summary>The bucket for an install instant and the current instant.</summary>
    public static string Bucket(DateTime firstSeenUtc, DateTime nowUtc) =>
        Bucket(DaysSince(firstSeenUtc, nowUtc));
}
