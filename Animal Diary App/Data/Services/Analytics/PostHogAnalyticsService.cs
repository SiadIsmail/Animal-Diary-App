namespace Animal_Diary_App.Data.Services.Analytics;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Animal_Diary_App.Helpers;

/// <summary>
/// The one and only PostHog-aware type in the app. It turns an explicit product
/// event into a minimal PostHog "capture" payload and POSTs it to the EU ingestion
/// endpoint. Nothing above this class knows PostHog exists — callers hold
/// <see cref="IAnalyticsService"/>.
///
/// <b>Why a hand-rolled HTTP capture instead of a PostHog SDK?</b> It is the strongest
/// possible privacy-by-design guarantee: the payload is built here, field by field, so
/// autocapture, session recording, screen tracking, <c>$device_id</c>, OS/version
/// fingerprinting, and IP geolocation don't have to be "turned off" — they cannot
/// happen, because we never write them. Only the fields below are ever sent.
///
/// Every event also carries two hard privacy switches:
/// <list type="bullet">
///   <item><c>$process_person_profile = false</c> — PostHog treats the event as
///   anonymous and never builds or updates a person profile.</item>
///   <item><c>$geoip_disable = true</c> — PostHog does not derive location/<c>$geoip_*</c>
///   from the request IP.</item>
/// </list>
///
/// Delivery is best-effort fire-and-forget: <see cref="Track"/> returns instantly and
/// the POST runs off-thread through <see cref="TaskExtensions.Forget"/>. Any failure
/// (offline, timeout, bad response) is swallowed and logged — analytics must never
/// block the UI or crash the app. There is no offline retry queue in v1; a dropped
/// event is acceptable for product telemetry.
/// </summary>
public sealed class PostHogAnalyticsService : IAnalyticsService
{
    // One shared client for the app's lifetime with a short timeout — a slow network
    // must not pile up connections or delay anything user-visible.
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _captureUrl = $"{AnalyticsConfig.Host.TrimEnd('/')}/capture/";
    private readonly bool _enabled;

    public PostHogAnalyticsService()
    {
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

        // Touch the anonymous id so it is created and persisted up front rather than on
        // the first event (keeps the first Track's off-thread work minimal).
        _ = AnalyticsIdentity.AnonymousId;
        return Task.CompletedTask;
    }

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(eventName))
            return;

        // Snapshot everything that could touch UI/MAUI singletons here on the caller's
        // thread; the send itself runs off-thread and must not read app state.
        var payload = BuildPayload(eventName, properties);
        SendAsync(payload).Forget();
    }

    // Build the exact JSON PostHog's /capture/ endpoint expects. Only these fields are
    // ever transmitted — see the class summary for the privacy rationale.
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
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
        };

        return JsonSerializer.Serialize(body);
    }

    private async Task SendAsync(string json)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(_captureUrl, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                System.Diagnostics.Debug.WriteLine($"[Analytics] capture returned {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            // Offline / timeout / DNS — telemetry is best-effort, so drop and log.
            System.Diagnostics.Debug.WriteLine($"[Analytics] send failed: {ex.Message}");
        }
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
}
