namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Analytics;
using Xunit;

/// <summary>The offline buffer. What matters here is that a transient failure never loses
/// an event (a dropped middle step reads as funnel drop-off), that a permanently rejected
/// payload cannot wedge the queue behind it, and that chronological order survives a
/// partial drain.</summary>
public class AnalyticsEventQueueTests
{
    private static AnalyticsEventQueue Build(out FakeAnalyticsQueueStore store)
    {
        store = new FakeAnalyticsQueueStore();
        return new AnalyticsEventQueue(store);
    }

    [Fact]
    public async Task Enqueue_Persists()
    {
        var queue = Build(out var store);

        await queue.EnqueueAsync("{\"event\":\"a\"}");

        Assert.Equal(1, await queue.CountAsync());
        Assert.NotNull(store.Contents);   // survives process death, which is the point
    }

    [Fact]
    public async Task Enqueue_IgnoresBlankPayloads()
    {
        var queue = Build(out _);

        await queue.EnqueueAsync("");
        await queue.EnqueueAsync("   ");

        Assert.Equal(0, await queue.CountAsync());
    }

    [Fact]
    public async Task Drain_SendsOldestFirst_AndEmpties()
    {
        var queue = Build(out _);
        await queue.EnqueueAsync("a");
        await queue.EnqueueAsync("b");
        await queue.EnqueueAsync("c");

        var seen = new List<string>();
        var sent = await queue.DrainAsync(p =>
        {
            seen.Add(p);
            return Task.FromResult(AnalyticsSendOutcome.Sent);
        });

        Assert.Equal(3, sent);
        Assert.Equal(new[] { "a", "b", "c" }, seen);
        Assert.Equal(0, await queue.CountAsync());
    }

    [Fact]
    public async Task Drain_StopsAtFirstRetry_AndKeepsTheRemainderInOrder()
    {
        var queue = Build(out _);
        await queue.EnqueueAsync("a");
        await queue.EnqueueAsync("b");
        await queue.EnqueueAsync("c");

        // "a" goes; the network dies on "b"; "c" must not be burned against it.
        var attempts = new List<string>();
        var sent = await queue.DrainAsync(p =>
        {
            attempts.Add(p);
            return Task.FromResult(p == "a" ? AnalyticsSendOutcome.Sent : AnalyticsSendOutcome.Retry);
        });

        Assert.Equal(1, sent);
        Assert.Equal(new[] { "a", "b" }, attempts);
        Assert.Equal(2, await queue.CountAsync());

        // The next drain resumes exactly where it left off.
        var resumed = new List<string>();
        await queue.DrainAsync(p =>
        {
            resumed.Add(p);
            return Task.FromResult(AnalyticsSendOutcome.Sent);
        });

        Assert.Equal(new[] { "b", "c" }, resumed);
        Assert.Equal(0, await queue.CountAsync());
    }

    [Fact]
    public async Task Drain_DiscardsPermanentlyRejectedPayloads()
    {
        var queue = Build(out _);
        await queue.EnqueueAsync("bad");
        await queue.EnqueueAsync("good");

        // A 4xx payload would fail forever; it must not block "good" behind it.
        var sent = await queue.DrainAsync(p => Task.FromResult(
            p == "bad" ? AnalyticsSendOutcome.Drop : AnalyticsSendOutcome.Sent));

        Assert.Equal(1, sent);                    // only "good" counted as delivered
        Assert.Equal(0, await queue.CountAsync()); // and nothing is left owed
    }

    [Fact]
    public async Task Drain_TreatsAThrowingSenderAsRetry()
    {
        var queue = Build(out _);
        await queue.EnqueueAsync("a");

        var sent = await queue.DrainAsync(_ => throw new InvalidOperationException("boom"));

        Assert.Equal(0, sent);
        Assert.Equal(1, await queue.CountAsync());   // kept, not lost
    }

    [Fact]
    public async Task Drain_OnEmptyQueue_DoesNothing()
    {
        var queue = Build(out _);

        var calls = 0;
        var sent = await queue.DrainAsync(_ => { calls++; return Task.FromResult(AnalyticsSendOutcome.Sent); });

        Assert.Equal(0, sent);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Drain_IsBoundedPerPass()
    {
        var queue = Build(out _);
        for (var i = 0; i < AnalyticsEventQueue.MaxPerDrain + 5; i++)
            await queue.EnqueueAsync($"e{i}");

        var calls = 0;
        await queue.DrainAsync(_ => { calls++; return Task.FromResult(AnalyticsSendOutcome.Sent); });

        // One pass must not sit on a slow network forever; the rest waits for the next.
        Assert.Equal(AnalyticsEventQueue.MaxPerDrain, calls);
        Assert.Equal(5, await queue.CountAsync());
    }

    [Fact]
    public async Task Overflow_DropsOldest_KeepingTheMostRecent()
    {
        var queue = Build(out _);
        for (var i = 0; i < AnalyticsEventQueue.MaxEvents + 3; i++)
            await queue.EnqueueAsync($"e{i}");

        Assert.Equal(AnalyticsEventQueue.MaxEvents, await queue.CountAsync());

        var seen = new List<string>();
        await queue.DrainAsync(p => { seen.Add(p); return Task.FromResult(AnalyticsSendOutcome.Sent); });

        // e0..e2 were evicted: a stale event is worth less than a recent one.
        Assert.Equal("e3", seen[0]);
    }

    [Fact]
    public async Task LoadsWhatAPreviousProcessLeftBehind()
    {
        var store = new FakeAnalyticsQueueStore { Contents = "[\"a\",\"b\"]" };
        var queue = new AnalyticsEventQueue(store);

        Assert.Equal(2, await queue.CountAsync());
    }

    [Fact]
    public async Task CorruptBufferStartsClean()
    {
        // An interrupted write must not fail every future send behind an unparseable file.
        var store = new FakeAnalyticsQueueStore { Contents = "{ this is not json" };
        var queue = new AnalyticsEventQueue(store);

        Assert.Equal(0, await queue.CountAsync());
        await queue.EnqueueAsync("a");
        Assert.Equal(1, await queue.CountAsync());
    }

    [Theory]
    [InlineData(3, 5, 3)]   // under the cap: untouched
    [InlineData(5, 5, 5)]   // exactly at it
    [InlineData(9, 5, 5)]   // over: trimmed to the newest
    public void Trim_KeepsTheNewest(int count, int max, int expected)
    {
        var items = Enumerable.Range(0, count).Select(i => $"e{i}").ToList();

        var trimmed = AnalyticsEventQueue.Trim(items, max);

        Assert.Equal(expected, trimmed.Count);
        Assert.Equal($"e{count - 1}", trimmed[^1]);
    }

    [Fact]
    public void Trim_WithNonPositiveMax_IsEmpty()
    {
        Assert.Empty(AnalyticsEventQueue.Trim(new List<string> { "a" }, 0));
    }
}

/// <summary>In-memory queue store, no file system.</summary>
internal sealed class FakeAnalyticsQueueStore : IAnalyticsQueueStore
{
    public string? Contents { get; set; }

    public Task<string?> ReadAsync() => Task.FromResult(Contents);

    public Task WriteAsync(string contents)
    {
        Contents = contents;
        return Task.CompletedTask;
    }
}
