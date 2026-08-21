namespace Animal_Diary_App.Data.Services.Analytics;

using System.Text.Json;

/// <summary>What happened to one attempted send, and therefore what to do with it next.</summary>
public enum AnalyticsSendOutcome
{
    /// <summary>Accepted by the server. Forget it.</summary>
    Sent,
    /// <summary>Transient failure (offline, timeout, 5xx, 429). Keep it and try later.</summary>
    Retry,
    /// <summary>Permanently unacceptable (4xx: bad key, malformed body). Retrying would
    /// fail forever, so discard rather than block the queue behind it.</summary>
    Drop,
}

/// <summary>
/// A small, bounded, persistent buffer of capture payloads that could not be delivered.
///
/// <para><b>Why it exists.</b> <c>Track</c> was fire-and-forget with no retry, so any
/// event raised while offline vanished. For a single metric that costs one count; in an
/// <i>ordered funnel</i> it is much worse, because a dropped middle step truncates that
/// user at the previous step: dropped events are indistinguishable from drop-off. This is
/// an offline-first app for people who log at vet visits and in waiting rooms, so that was
/// the largest source of funnel error.</para>
///
/// <para><b>Shape.</b> Payloads are stored exactly as they were built for transmission,
/// this class never inspects, enriches, or rewrites them, so it cannot widen what the
/// analytics contract permits to be collected. Oldest first, capped at
/// <see cref="MaxEvents"/>; when full the <i>oldest</i> are dropped, because a stale event
/// is worth less than the one describing what the user just did.</para>
///
/// <para><b>Delivery is at-least-once.</b> A send whose response never arrives is retried,
/// so the same payload can reach PostHog twice. That is why every payload carries a
/// per-event <c>uuid</c> (see <c>PostHogAnalyticsService</c>): the server deduplicates on
/// it, which is what keeps retries from inflating funnel counts.</para>
///
/// <para>Holds no MAUI or file-system types (storage is behind
/// <see cref="IAnalyticsQueueStore"/>), so the buffering logic is unit-testable.</para>
/// </summary>
public sealed class AnalyticsEventQueue
{
    /// <summary>Cap on buffered payloads. Deliberately small: this is a courtesy buffer for
    /// a flaky connection, not a store-and-forward log. At ~400 bytes a payload this is a
    /// ~20 KB file in the worst case.</summary>
    public const int MaxEvents = 50;

    /// <summary>Most payloads sent per drain. Bounds how long one drain can run (and thus
    /// how long a fresh event waits behind it) when a large backlog meets a slow network;
    /// the remainder goes on the next drain.</summary>
    public const int MaxPerDrain = 20;

    private readonly IAnalyticsQueueStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<string>? _pending;

    public AnalyticsEventQueue(IAnalyticsQueueStore store) => _store = store;

    /// <summary>Buffer a payload that could not be delivered.</summary>
    public async Task EnqueueAsync(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = await LoadLockedAsync().ConfigureAwait(false);
            pending.Add(payloadJson);
            _pending = Trim(pending, MaxEvents);
            await SaveLockedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] enqueue failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Try to deliver the backlog, oldest first, stopping at the first transient failure
    /// (there is no point burning the rest of the batch against a network that just refused
    /// one). Returns how many were accepted.
    ///
    /// <para>The batch is taken out of the queue under the lock and sent <b>outside</b> it,
    /// so a drain over a slow connection never blocks a fresh <see cref="EnqueueAsync"/>.
    /// Anything unsent goes back at the front, ahead of whatever arrived meanwhile, so
    /// chronological order survives.</para>
    /// </summary>
    /// <param name="sender">Delivers one payload and classifies the result.</param>
    public async Task<int> DrainAsync(Func<string, Task<AnalyticsSendOutcome>> sender)
    {
        List<string> batch;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = await LoadLockedAsync().ConfigureAwait(false);
            if (pending.Count == 0)
                return 0;

            batch = pending.Take(MaxPerDrain).ToList();
            _pending = pending.Skip(batch.Count).ToList();
            await SaveLockedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] drain load failed: {ex.Message}");
            return 0;
        }
        finally
        {
            _gate.Release();
        }

        var sent = 0;
        var index = 0;
        for (; index < batch.Count; index++)
        {
            AnalyticsSendOutcome outcome;
            try
            {
                outcome = await sender(batch[index]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A throwing sender is a bug, but the payload is not at fault: keep it.
                System.Diagnostics.Debug.WriteLine($"[Analytics] drain send threw: {ex.Message}");
                outcome = AnalyticsSendOutcome.Retry;
            }

            if (outcome == AnalyticsSendOutcome.Retry)
                break;

            if (outcome == AnalyticsSendOutcome.Sent)
                sent++;
        }

        // Everything from the first Retry onwards is still owed.
        if (index < batch.Count)
            await RequeueFrontAsync(batch.Skip(index).ToList()).ConfigureAwait(false);

        return sent;
    }

    /// <summary>Buffered payload count. Diagnostics and tests.</summary>
    public async Task<int> CountAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return (await LoadLockedAsync().ConfigureAwait(false)).Count;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Keep the newest <paramref name="max"/> payloads, preserving order. Pure so
    /// the overflow rule is testable without storage.</summary>
    public static List<string> Trim(List<string> items, int max)
    {
        if (max <= 0)
            return new List<string>();

        return items.Count <= max
            ? items
            : items.Skip(items.Count - max).ToList();
    }

    private async Task RequeueFrontAsync(List<string> unsent)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var pending = await LoadLockedAsync().ConfigureAwait(false);
            unsent.AddRange(pending);          // older items first, then anything new
            _pending = Trim(unsent, MaxEvents);
            await SaveLockedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] requeue failed: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Locked helpers: callers must hold _gate ───────────────────────────────
    private async Task<List<string>> LoadLockedAsync()
    {
        if (_pending is not null)
            return _pending;

        var raw = await _store.ReadAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return _pending = new List<string>();

        try
        {
            _pending = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
        }
        catch (JsonException)
        {
            // Corrupt buffer (interrupted write, manual edit): start clean rather than
            // fail every future send behind an unparseable file.
            _pending = new List<string>();
        }

        return _pending;
    }

    private async Task SaveLockedAsync()
    {
        var json = JsonSerializer.Serialize(_pending ?? new List<string>());
        await _store.WriteAsync(json).ConfigureAwait(false);
    }
}
