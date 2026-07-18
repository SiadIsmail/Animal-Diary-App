namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// Owns the app's <b>anonymous</b> analytics identifier — the only id ever attached
/// to an event.
///
/// What it is: a random <see cref="System.Guid"/> generated on first use and kept in
/// device <see cref="Preferences"/>. It is <b>not</b> derived from anything — no
/// email, no account, no pet, no device/advertising id. It exists purely so PostHog
/// can group a single install's events for funnel/retention counting; it links to no
/// real-world identity and to no personal data (events carry none — see the docs).
///
/// Privacy lifecycle: <see cref="Rotate"/> throws the id away and mints a fresh one.
/// It is called from the "delete all data" reset so a wiped device starts a brand-new
/// anonymous identity, exactly as a first install would — past events can no longer be
/// associated with the fresh id.
/// </summary>
public static class AnalyticsIdentity
{
    private const string AnonymousIdKey = "analytics_anonymous_id";
    private static readonly object Gate = new();
    private static string? _cached;

    /// <summary>The current anonymous id, creating and persisting one on first read.</summary>
    public static string AnonymousId
    {
        get
        {
            lock (Gate)
            {
                if (!string.IsNullOrEmpty(_cached))
                    return _cached!;

                var stored = Preferences.Default.Get(AnonymousIdKey, string.Empty);
                if (string.IsNullOrEmpty(stored))
                {
                    stored = Guid.NewGuid().ToString("N");
                    Preferences.Default.Set(AnonymousIdKey, stored);
                }

                _cached = stored;
                return stored;
            }
        }
    }

    /// <summary>Discard the current anonymous id and generate a fresh one. Used by the
    /// data-reset path so a reset unlinks the install from its prior anonymous id.</summary>
    public static void Rotate()
    {
        lock (Gate)
        {
            var fresh = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(AnonymousIdKey, fresh);
            _cached = fresh;
        }
    }
}
