namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// Decides when a foreground appearance counts as a <b>new session</b>, i.e. when
/// <c>app_opened</c> should fire.
///
/// <para><b>Why this exists.</b> <c>app_opened</c> used to fire from
/// <c>App.StartAsync</c>, which is wrong at both ends. Too many: a reboot or a Play
/// Store update starts the process <i>headlessly</i> (the boot receiver resolves
/// services, which constructs <c>App</c>) with no window and no user, so reboots were
/// counted as launches. Too few: <c>StartAsync</c> runs once per process, so a user who
/// foregrounds the app daily for a week without the process being killed produced a
/// single event. Both directions corrupt the "did they come back" step of the funnel.</para>
///
/// <para>The fix is two gates. <i>Where</i> it fires moved to window/resume touchpoints
/// (a real Activity exists by definition) — see <c>App.CreateWindow</c> /
/// <c>App.OnResume</c>. <i>Whether</i> it fires is this class: a standard idle-timeout
/// session window, so an Activity recreation or a quick app-switch continues the current
/// session while a genuine return the next day starts a new one.</para>
///
/// <para>Deliberately pure — no MAUI, no <c>Preferences</c>, no clock of its own — so the
/// decision is unit-testable. Persistence of the last-activity stamp lives in
/// <see cref="AnalyticsIdentity"/>, which already owns the analytics
/// <c>Preferences</c> state.</para>
/// </summary>
public static class AnalyticsSession
{
    /// <summary>How long the app must be idle (backgrounded or dead) before the next
    /// appearance counts as a new session. 30 minutes is the common analytics default:
    /// long enough that switching apps to check a text mid-log doesn't split one visit
    /// into two, short enough that a return later the same day is counted.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when <paramref name="nowUtc"/> begins a new session.
    /// </summary>
    /// <param name="lastActivityUtc">When the app was last seen alive and foreground,
    /// or null if it never has been (first ever launch → always a new session).</param>
    /// <param name="nowUtc">The current instant, UTC.</param>
    /// <param name="idleTimeout">Override for the idle window; defaults to
    /// <see cref="IdleTimeout"/>. Exists for tests.</param>
    public static bool IsNewSession(DateTime? lastActivityUtc, DateTime nowUtc, TimeSpan? idleTimeout = null)
    {
        if (lastActivityUtc is not DateTime last)
            return true;

        var elapsed = nowUtc - last;

        // Clock moved backwards (timezone/manual change, or a restored backup). We cannot
        // reason about the gap, so start a fresh session rather than suppress events
        // indefinitely — under-counting a return is worse than over-counting one.
        if (elapsed < TimeSpan.Zero)
            return true;

        return elapsed >= (idleTimeout ?? IdleTimeout);
    }
}
