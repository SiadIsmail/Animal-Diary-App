namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Billing;
using Xunit;

public class TrialServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (TrialService svc, FakeTrialStore store, Box<DateTime> clock) Build(DateTime? start = null)
    {
        var store = new FakeTrialStore { Start = start };
        var clock = new Box<DateTime>(T0);
        var svc = new TrialService(store, () => clock.Value);
        return (svc, store, clock);
    }

    [Fact]
    public async Task NotStarted_IsInactive_AndZeroRemaining()
    {
        var (svc, _, _) = Build(start: null);
        await svc.InitializeAsync();

        Assert.False(svc.IsActive);
        Assert.Equal(TimeSpan.Zero, svc.TimeRemaining);
        Assert.Equal(0, svc.DaysLeft);
    }

    [Fact]
    public async Task EnsureStarted_StartsOnce_AndPersists()
    {
        var (svc, store, _) = Build(start: null);

        Assert.True(await svc.EnsureStartedAsync());   // first call actually starts it
        Assert.False(await svc.EnsureStartedAsync());  // idempotent thereafter
        Assert.Equal(T0, store.Start);                 // persisted at the clock's instant
        Assert.True(svc.IsActive);
    }

    [Fact]
    public async Task WithinWindow_IsActive()
    {
        var (svc, _, clock) = Build(start: T0);
        await svc.InitializeAsync();

        clock.Value = T0 + BillingConfig.TrialLength - TimeSpan.FromSeconds(1);
        Assert.True(svc.IsActive);
        Assert.True(svc.TimeRemaining > TimeSpan.Zero);
    }

    [Fact]
    public async Task AtOrAfterWindow_IsExpired()
    {
        var (svc, _, clock) = Build(start: T0);
        await svc.InitializeAsync();

        clock.Value = T0 + BillingConfig.TrialLength;            // exactly elapsed
        Assert.False(svc.IsActive);
        Assert.Equal(TimeSpan.Zero, svc.TimeRemaining);

        clock.Value = T0 + BillingConfig.TrialLength + TimeSpan.FromHours(1);
        Assert.False(svc.IsActive);
    }

    [Fact]
    public async Task DaysLeft_RoundsUp()
    {
        // Only meaningful when the configured trial is at least a couple of days.
        if (BillingConfig.TrialLength < TimeSpan.FromDays(2))
            return;

        var (svc, _, clock) = Build(start: T0);
        await svc.InitializeAsync();

        // 1.2 days remaining → "2 days left".
        clock.Value = T0 + BillingConfig.TrialLength - TimeSpan.FromDays(1.2);
        Assert.Equal(2, svc.DaysLeft);
    }
}

/// <summary>Mutable holder so the injected clock lambda can be advanced from a test.</summary>
internal sealed class Box<T>
{
    public Box(T value) => Value = value;
    public T Value { get; set; }
}
