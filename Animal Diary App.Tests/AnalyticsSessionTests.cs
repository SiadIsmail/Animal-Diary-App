namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Analytics;
using Xunit;

/// <summary>The gate that decides whether an appearance counts as a new <c>app_opened</c>.
/// These cases are the difference between counting Activity recreations as launches and
/// missing a genuine next-day return: both of which corrupt the funnel's entry step.</summary>
public class AnalyticsSessionTests
{
    private static readonly DateTime T0 = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstEverAppearance_IsNewSession()
    {
        Assert.True(AnalyticsSession.IsNewSession(lastActivityUtc: null, nowUtc: T0));
    }

    [Fact]
    public void WithinIdleWindow_ContinuesSession()
    {
        // An Activity recreation (memory pressure, rotation) or a quick app-switch.
        var now = T0 + AnalyticsSession.IdleTimeout - TimeSpan.FromSeconds(1);
        Assert.False(AnalyticsSession.IsNewSession(T0, now));
    }

    [Fact]
    public void ImmediateReappearance_ContinuesSession()
    {
        Assert.False(AnalyticsSession.IsNewSession(T0, T0));
    }

    [Fact]
    public void AtIdleWindow_StartsNewSession()
    {
        Assert.True(AnalyticsSession.IsNewSession(T0, T0 + AnalyticsSession.IdleTimeout));
    }

    [Fact]
    public void NextDayReturn_StartsNewSession()
    {
        // The case the whole "came back later" funnel step depends on.
        Assert.True(AnalyticsSession.IsNewSession(T0, T0.AddDays(1)));
    }

    [Fact]
    public void ClockMovedBackwards_StartsNewSession()
    {
        // Timezone change or a restored backup: we cannot reason about the gap, so open a
        // session rather than suppress events until the clock catches up.
        Assert.True(AnalyticsSession.IsNewSession(T0, T0 - TimeSpan.FromHours(5)));
    }

    [Fact]
    public void IdleTimeoutIsOverridable()
    {
        var now = T0 + TimeSpan.FromMinutes(2);
        Assert.False(AnalyticsSession.IsNewSession(T0, now, TimeSpan.FromMinutes(5)));
        Assert.True(AnalyticsSession.IsNewSession(T0, now, TimeSpan.FromMinutes(1)));
    }
}

/// <summary>The <c>days_since_install</c> bucket: the property that turns "return on a
/// later day" from an inexpressible funnel constraint into a filter.</summary>
public class AnalyticsTenureTests
{
    private static readonly DateTime Install = new(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, AnalyticsTenure.BucketDay0)]
    [InlineData(1, AnalyticsTenure.BucketDay1)]
    [InlineData(2, AnalyticsTenure.BucketDays2To3)]
    [InlineData(3, AnalyticsTenure.BucketDays2To3)]
    [InlineData(4, AnalyticsTenure.BucketDays4To7)]
    [InlineData(7, AnalyticsTenure.BucketDays4To7)]
    [InlineData(8, AnalyticsTenure.BucketDays8To14)]
    [InlineData(14, AnalyticsTenure.BucketDays8To14)]
    [InlineData(15, AnalyticsTenure.BucketDays15Plus)]
    [InlineData(365, AnalyticsTenure.BucketDays15Plus)]
    public void BucketsByDayCount(int days, string expected)
    {
        Assert.Equal(expected, AnalyticsTenure.Bucket(days));
    }

    [Fact]
    public void SameCalendarDay_IsDayZero()
    {
        Assert.Equal(AnalyticsTenure.BucketDay0, AnalyticsTenure.Bucket(Install, Install.AddMinutes(30)));
    }

    [Fact]
    public void CountsCalendarDays_NotElapsedHours()
    {
        // Installed 23:00, returned 01:00: two hours later, but the next UTC day, which
        // is the D1 convention the buckets are documented to follow.
        Assert.Equal(AnalyticsTenure.BucketDay1, AnalyticsTenure.Bucket(Install, Install.AddHours(2)));
    }

    [Fact]
    public void TwoWeekBoundaryLandsInTheExpectedBuckets()
    {
        // Day 14 is the last day inside 8-14; anything past it reads as 15+.
        Assert.Equal(AnalyticsTenure.BucketDays8To14, AnalyticsTenure.Bucket(Install, Install.AddDays(14)));
        Assert.Equal(AnalyticsTenure.BucketDays15Plus, AnalyticsTenure.Bucket(Install, Install.AddDays(15)));
    }

    [Fact]
    public void BackwardsClock_ClampsToDayZero()
    {
        Assert.Equal(AnalyticsTenure.BucketDay0, AnalyticsTenure.Bucket(Install, Install.AddDays(-3)));
        Assert.Equal(0, AnalyticsTenure.DaysSince(Install, Install.AddDays(-3)));
    }
}
