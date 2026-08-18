namespace Animal_Diary_App.Data.Services.Analytics;

using System.Globalization;

/// <summary>
/// Owns the per-install analytics bookkeeping kept in device <see cref="Preferences"/>:
/// the <b>anonymous</b> identifier, the install instant, and the last-activity stamp.
///
/// What the id is: a random <see cref="System.Guid"/> generated on first use. It is
/// <b>not</b> derived from anything — no email, no account, no pet, no device/advertising
/// id. It exists purely so PostHog can group a single install's events for funnel
/// counting; it links to no real-world identity and to no personal data (events carry
/// none — see the docs).
///
/// The other two values are timing only, and neither is transmitted: the install instant
/// is transmitted solely as a coarse <see cref="AnalyticsTenure"/> bucket, and the
/// last-activity stamp never leaves the device at all — it only decides whether the next
/// appearance counts as a new session (<see cref="AnalyticsSession"/>).
///
/// Privacy lifecycle: <see cref="Rotate"/> throws all three away and starts fresh. It is
/// called from the "delete all data" reset so a wiped device starts a brand-new anonymous
/// identity, exactly as a first install would — past events can no longer be associated
/// with the fresh id, and the install clock restarts with it so tenure can't be used to
/// bridge the two.
/// </summary>
public static class AnalyticsIdentity
{
    private const string AnonymousIdKey = "analytics_anonymous_id";
    private const string FirstSeenKey = "analytics_first_seen_utc";
    private const string LastActivityKey = "analytics_last_activity_utc";

    private static readonly object Gate = new();
    private static string? _cached;
    private static DateTime? _cachedFirstSeen;

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

    /// <summary>
    /// When this install was first seen, UTC — the anchor for <c>days_since_install</c>.
    /// Stamped on first read (so an install that upgrades into this build simply starts
    /// its clock now, reading as day 0 rather than as a missing value). Cached, because
    /// every event's payload reads it.
    /// </summary>
    public static DateTime FirstSeenUtc
    {
        get
        {
            lock (Gate)
            {
                if (_cachedFirstSeen is DateTime cached)
                    return cached;

                var stored = Preferences.Default.Get(FirstSeenKey, string.Empty);
                if (TryParseUtc(stored) is DateTime parsed)
                    return (_cachedFirstSeen = parsed).Value;

                var now = DateTime.UtcNow;
                Preferences.Default.Set(FirstSeenKey, Format(now));
                return (_cachedFirstSeen = now).Value;
            }
        }
    }

    /// <summary>The last time the app was seen alive and foreground, or null if never.
    /// Device-local bookkeeping for the session gate; never transmitted.</summary>
    public static DateTime? LastActivityUtc
    {
        get
        {
            lock (Gate)
                return TryParseUtc(Preferences.Default.Get(LastActivityKey, string.Empty));
        }
    }

    /// <summary>Record that the app is alive and foreground right now, without starting a
    /// session. Called when the app is backgrounded so the idle clock runs from the moment
    /// the user actually left, not from the last event.</summary>
    public static void TouchActivity()
    {
        lock (Gate)
            Preferences.Default.Set(LastActivityKey, Format(DateTime.UtcNow));
    }

    /// <summary>
    /// Stamp activity and report whether this appearance <b>begins a new session</b> (per
    /// <see cref="AnalyticsSession.IdleTimeout"/>), so the caller can fire
    /// <c>app_opened</c> exactly once per session. Always stamps, whatever it returns —
    /// an appearance is activity either way.
    /// </summary>
    public static bool TryBeginSession()
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            var last = TryParseUtc(Preferences.Default.Get(LastActivityKey, string.Empty));
            var isNew = AnalyticsSession.IsNewSession(last, now);
            Preferences.Default.Set(LastActivityKey, Format(now));
            return isNew;
        }
    }

    /// <summary>Discard the anonymous id, the install instant, and the session stamp, then
    /// mint a fresh id. Used by the data-reset path so a reset unlinks the install from its
    /// prior anonymous id and restarts its tenure clock.</summary>
    public static void Rotate()
    {
        lock (Gate)
        {
            var fresh = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(AnonymousIdKey, fresh);
            _cached = fresh;

            // A reset is a new install as far as analytics is concerned: restart the
            // tenure clock, and drop the session stamp so the next appearance opens a
            // clean session rather than continuing the wiped identity's one.
            var now = DateTime.UtcNow;
            Preferences.Default.Set(FirstSeenKey, Format(now));
            _cachedFirstSeen = now;
            Preferences.Default.Remove(LastActivityKey);
        }
    }

    // Round-trip ("o") in invariant culture: parsed by the same pair of methods on the
    // same device, but a user switching device language must not make a stamp unreadable.
    private static string Format(DateTime utc) =>
        utc.ToString("o", CultureInfo.InvariantCulture);

    private static DateTime? TryParseUtc(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
            return null;

        // RoundtripKind is mutually exclusive with AdjustToUniversal/AssumeLocal/
        // AssumeUniversal — combining them throws ArgumentException on every call.
        // Format() writes "o" with a trailing "Z", so RoundtripKind alone already
        // yields DateTimeKind.Utc.
        return DateTime.TryParse(
            stored,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : null;
    }
}
