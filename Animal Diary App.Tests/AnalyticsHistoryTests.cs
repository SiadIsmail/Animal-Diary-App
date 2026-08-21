namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Analytics;

using Xunit;

/// <summary>
/// The <c>days_of_history</c> buckets: the one measurement that can falsify the paid
/// boundary's central claim (willingness to pay rises with accumulated history).
///
/// <para>Worth testing because an off-by-one here is completely invisible. Nothing
/// crashes, no chart looks wrong, and a cohort simply sits in the neighbouring column for
/// as long as nobody checks, which is exactly the kind of quiet error that leads to a
/// pricing decision made on a number that was never true.</para>
/// </summary>
public class AnalyticsHistoryTests
{
    [Theory]
    [InlineData(0, AnalyticsHistory.None)]
    [InlineData(1, AnalyticsHistory.FirstWeek)]
    [InlineData(7, AnalyticsHistory.FirstWeek)]
    [InlineData(8, AnalyticsHistory.FirstMonth)]
    [InlineData(30, AnalyticsHistory.FirstMonth)]
    [InlineData(31, AnalyticsHistory.Months2To3)]
    [InlineData(90, AnalyticsHistory.Months2To3)]
    [InlineData(91, AnalyticsHistory.Months4To6)]
    [InlineData(180, AnalyticsHistory.Months4To6)]
    [InlineData(181, AnalyticsHistory.Beyond)]
    [InlineData(4000, AnalyticsHistory.Beyond)]
    public void EveryBoundaryLandsInTheExpectedBucket(int days, string expected)
    {
        Assert.Equal(expected, AnalyticsHistory.Bucket(days));
    }

    [Fact]
    public void TheBucketsAreContiguousAndTotalToSix()
    {
        // No gap and no overlap: a day that belongs to no bucket, or to two, would be a
        // silent hole in the only curve this decision gets read from.
        var seen = Enumerable.Range(0, 400).Select(AnalyticsHistory.Bucket).Distinct().ToList();
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void The_month_boundary_that_the_whole_claim_turns_on_is_where_it_should_be()
    {
        // "Month-4+ converts at several times month-1" is the sentence this property
        // exists to answer, so 90/91 is the one boundary that must not drift.
        Assert.Equal(AnalyticsHistory.Months2To3, AnalyticsHistory.Bucket(90));
        Assert.Equal(AnalyticsHistory.Months4To6, AnalyticsHistory.Bucket(91));
    }

    [Fact]
    public void ADeviceClockMovedBackwardsReadsAsNoHistory()
    {
        // Telemetry may never be the thing that throws inside a purchase.
        Assert.Equal(0, AnalyticsHistory.DaysBetween(new DateTime(2026, 6, 1), new DateTime(2026, 5, 1)));
        Assert.Equal(AnalyticsHistory.None, AnalyticsHistory.Bucket(-40));
    }

    [Fact]
    public void DaysAreCountedByCalendarDayNotElapsedHours()
    {
        // First entry late on the 1st, "today" early on the 3rd, is two days of history,
        // not one, which is what an elapsed-hours subtraction would report.
        var first = new DateTime(2026, 5, 1, 23, 40, 0);
        var today = new DateTime(2026, 5, 3, 0, 10, 0);

        Assert.Equal(2, AnalyticsHistory.DaysBetween(first, today));
    }

    [Fact]
    public void ItSharesNoBoundariesWithTheInstallAgeBuckets()
    {
        // AnalyticsTenure answers "is this install new" and tops out at 15+, so every
        // question this property exists to ask would land in its last bucket. If these two
        // ever converge, one of them has stopped answering its own question.
        Assert.NotEqual(AnalyticsHistory.Bucket(200), AnalyticsHistory.Bucket(100));
        Assert.Equal(AnalyticsHistory.Beyond, AnalyticsHistory.Bucket(200));
    }
}
