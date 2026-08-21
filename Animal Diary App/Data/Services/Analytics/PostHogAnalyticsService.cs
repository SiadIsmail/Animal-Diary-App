namespace Animal_Diary_App.Data.Services.Analytics;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Animal_Diary_App.Helpers;

/// <summary>
/// The one and only PostHog-aware type in the app. It turns an explicit product
/// event into a minimal PostHog "capture" payload and POSTs it to the EU ingestion
/// endpoint. Nothing above this class knows PostHog exists: callers hold
/// <see cref="IAnalyticsService"/>.
///
/// <b>Why a hand-rolled HTTP capture instead of a PostHog SDK?</b> It is the strongest
/// possible privacy-by-design guarantee: the payload is built here, field by field, so
/// autocapture, session recording, screen tracking, <c>$device_id</c>, OS/version
/// fingerprinting, and IP geolocation don't have to be "turned off": they cannot
/// happen, because we never write them. Only the fields below are ever sent.
///
/// Every event also carries two hard privacy switches:
/// <list type="bullet">
///   <item><c>$process_person_profile = false</c>: PostHog treats the event as
///   anonymous and never builds or updates a person profile.</item>
///   <item><c>$geoip_disable = true</c>: PostHog does not derive location/<c>$geoip_*</c>
///   from the request IP.</item>
/// </list>
///
/// <para><b>Delivery.</b> <see cref="Track"/> still returns instantly and never throws:
/// the payload is built on the caller's thread (so no UI/MAUI singleton is touched
/// off-thread) and the POST runs through <see cref="TaskExtensions.Forget"/>. What changed
/// is what happens when that POST fails. A transient failure: offline, timeout, 5xx,
/// now parks the payload in <see cref="AnalyticsEventQueue"/> and it is retried on the
/// next successful send, the next launch, or the next time connectivity returns. A
/// permanent failure (4xx: wrong key, malformed body) is discarded, because retrying it
/// forever would only block the events behind it.</para>
///
/// <para>Two fields exist to make that retry safe. <c>timestamp</c> is stamped when the
/// event <i>happened</i> and <c>sent_at</c> when it was actually transmitted, so PostHog
/// can correct for both the queue delay and a wrong device clock instead of filing a
/// week-old event as if it just occurred: ordering matters when the data is read as a
/// funnel. <c>uuid</c> is a fresh random GUID per event, so a payload that is retried
/// after an ambiguous failure is deduplicated server-side rather than counted twice. It
/// is per-<i>event</i>, not per-user: it identifies nothing and links to nothing.</para>
/// </summary>
public sealed class PostHogAnalyticsService : IAnalyticsService
{
    // One shared client for the app's lifetime with a short timeout: a slow network
    // must not pile up connections or delay anything user-visible.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _captureUrl = $"{AnalyticsConfig.Host.TrimEnd('/')}/capture/";
    private readonly bool _enabled;
    private readonly AnalyticsEventQueue _queue;

    // 0/1 rather than a lock: a drain can be triggered from a successful send, from
    // startup, and from a connectivity change at once, and they must not interleave.
    private int _draining;
    private bool _watchingConnectivity;

    public PostHogAnalyticsService(IAnalyticsQueueStore queueStore)
    {
        _queue = new AnalyticsEventQueue(queueStore);

        // Self-disable when the master switch is off or no project key is present, so a
        // key-less build simply never sends anything (and IsEnabled reports it).
        _enabled = AnalyticsConfig.Enabled
            && !string.IsNullOrWhiteSpace(AnalyticsConfig.ProjectApiKey)
            && AnalyticsConfig.ProjectApiKey.StartsWith("phc_", StringComparison.Ordinal);
    }

    public bool IsEnabled => _enabled;

    public Task InitializeAsync()
    {
        if (!_enabled)
            return Task.CompletedTask;

        // Touch the anonymous id and the install instant so both are created and persisted
        // up front rather than on the first event (keeps the first Track's work minimal,
        // and anchors days_since_install at launch rather than at whatever fired first).
        _ = AnalyticsIdentity.AnonymousId;
        _ = AnalyticsIdentity.FirstSeenUtc;

        // Anything stranded by a previous offline session goes out now. Deliberately not
        // awaited: startup must not wait on the network, and the drain is self-guarding.
        DrainAsync().Forget();

        // Coming back online is the moment a backlog can actually move. Subscribed once;
        // this service is a singleton for the app's lifetime, so there is nothing to
        // unsubscribe. Guarded because Connectivity throws on some test hosts.
        if (!_watchingConnectivity)
        {
            try
            {
                Microsoft.Maui.Networking.Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
                _watchingConnectivity = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Analytics] connectivity watch unavailable: {ex.Message}");
            }
        }

        return Task.CompletedTask;
    }

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(eventName))
            return;

        // Snapshot everything that could touch UI/MAUI singletons here on the caller's
        // thread; the send itself runs off-thread and must not read app state.
        var payload = BuildPayload(eventName, properties);
        DispatchAsync(payload).Forget();
    }

    private void OnConnectivityChanged(object? sender, Microsoft.Maui.Networking.ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet)
            DrainAsync().Forget();
    }

    // Send now; park it for later if the network (not the payload) was the problem.
    private async Task DispatchAsync(string payload)
    {
        var outcome = await SendAsync(payload).ConfigureAwait(false);

        switch (outcome)
        {
            case AnalyticsSendOutcome.Retry:
                await _queue.EnqueueAsync(payload).ConfigureAwait(false);
                break;

            case AnalyticsSendOutcome.Sent:
                // The network is up right now: the best moment to clear any backlog.
                await DrainAsync().ConfigureAwait(false);
                break;
        }
    }

    private async Task DrainAsync()
    {
        if (!_enabled)
            return;

        // Already draining: that pass will pick up anything newly queued.
        if (Interlocked.Exchange(ref _draining, 1) == 1)
            return;

        try
        {
            await _queue.DrainAsync(SendAsync).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] drain failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _draining, 0);
        }
    }

    // Build the exact JSON PostHog's /capture/ endpoint expects. Only these fields are
    // ever transmitted: see the class summary for the privacy rationale.
    private string BuildPayload(string eventName, IReadOnlyDictionary<string, object?>? properties)
    {
        var props = new Dictionary<string, object?>();

        // Caller-supplied, event-describing properties first.
        if (properties != null)
            foreach (var kvp in properties)
                props[kvp.Key] = kvp.Value;

        // Non-identifying context we deliberately choose to attach.
        props[AnalyticsEvents.PropAppVersion] = SafeAppVersion();
        props[AnalyticsEvents.PropPlatform] = SafePlatform();
        // Coarse signed-in/anonymous state on every event (see AnalyticsContext). This is
        // NOT an identity: it links to no account, only reports whether cloud is on, so
        // signed-in behaviour can be segmented without ever calling identify().
        props[AnalyticsEvents.PropAccountState] = AnalyticsContext.AccountState;
        // Which creator this install came through, on every event for the same reason as the
        // line above: it turns every existing funnel into one that can be split by channel,
        // without a second event stream. A channel label, never the code and never a person.
        props[AnalyticsEvents.PropReferralSource] = AnalyticsContext.ReferralSource;
        // Coarse install age. Carried by every event because a funnel cannot express "this
        // step must be at least a day after the previous one": as a property it becomes an
        // ordinary filter, which is what makes the "came back later" step measurable.
        props[AnalyticsEvents.PropDaysSinceInstall] = SafeTenureBucket();
        // Only set language if the caller didn't already (app_opened sends it explicitly).
        if (!props.ContainsKey(AnalyticsEvents.PropLanguage))
            props[AnalyticsEvents.PropLanguage] = LocalizationManager.Instance.CurrentLanguage;

        // Hard privacy switches on every event.
        props["$process_person_profile"] = false;
        props["$geoip_disable"] = true;

        var body = new Dictionary<string, object?>
        {
            ["api_key"] = AnalyticsConfig.ProjectApiKey,
            ["event"] = eventName,
            ["distinct_id"] = AnalyticsIdentity.AnonymousId,
            ["properties"] = props,
            // When it HAPPENED. sent_at (when it was transmitted) is added at send time.
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
            // Per-event random GUID so an at-least-once retry is deduplicated server-side.
            // Not an identifier: fresh per event, derived from nothing, links to nothing.
            ["uuid"] = Guid.NewGuid().ToString(),
        };

        return JsonSerializer.Serialize(body);
    }

    /// <summary>Deliver one already-built payload and classify the result so the caller
    /// knows whether to keep it. Never throws.</summary>
    private async Task<AnalyticsSendOutcome> SendAsync(string json)
    {
        try
        {
            using var content = new StringContent(StampSentAt(json), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(_captureUrl, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return AnalyticsSendOutcome.Sent;

            var status = (int)response.StatusCode;
            System.Diagnostics.Debug.WriteLine($"[Analytics] capture returned {status}");

            // 5xx / 408 / 429 are "try again"; every other 4xx means this payload will
            // never be accepted (bad key, malformed body) so keeping it helps nobody.
            return status >= 500 || status == 408 || status == 429
                ? AnalyticsSendOutcome.Retry
                : AnalyticsSendOutcome.Drop;
        }
        catch (Exception ex)
        {
            // Offline / timeout / DNS: the payload is fine, the network isn't.
            System.Diagnostics.Debug.WriteLine($"[Analytics] send failed: {ex.Message}");
            return AnalyticsSendOutcome.Retry;
        }
    }

    /// <summary>Stamp the moment of transmission. PostHog reads <c>sent_at</c> against its
    /// own receive time to correct <c>timestamp</c> for device clock skew and for however
    /// long the event sat in the offline queue.</summary>
    private static string StampSentAt(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject body)
            {
                body["sent_at"] = DateTime.UtcNow.ToString("o");
                return body.ToJsonString();
            }
        }
        catch (Exception ex)
        {
            // Never let skew correction cost us the event itself.
            System.Diagnostics.Debug.WriteLine($"[Analytics] sent_at stamp failed: {ex.Message}");
        }

        return json;
    }

    // AppInfo/DeviceInfo can throw on some platforms/test hosts; never let context
    // gathering break an event.
    private static string SafeAppVersion()
    {
        try { return AppInfo.Current.VersionString; }
        catch { return "unknown"; }
    }

    private static string SafePlatform()
    {
        try { return DeviceInfo.Current.Platform.ToString(); }
        catch { return "unknown"; }
    }

    private static string SafeTenureBucket()
    {
        try { return AnalyticsTenure.Bucket(AnalyticsIdentity.FirstSeenUtc, DateTime.UtcNow); }
        catch { return AnalyticsTenure.BucketDay0; }
    }
}
