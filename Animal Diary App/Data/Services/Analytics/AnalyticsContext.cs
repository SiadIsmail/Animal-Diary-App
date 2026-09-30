namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// The one piece of coarse, ambient context the payload builder needs but can't read
/// from itself: whether this install is currently signed in to cloud. It is a tiny
/// static flag (deliberately NOT an injected dependency) so the analytics subsystem
/// stays free of any Cloud type and the Cloud subsystem stays free of analytics beyond
/// its <see cref="IAnalyticsService"/> calls (the two worlds never reference each
/// other's classes, mirroring how <see cref="AnalyticsIdentity"/> is a leaf static).
///
/// <b>Privacy boundary (hard rule):</b> the only thing that may ever be stored here is a
/// boolean signed-in/anonymous state. No user id, email, token, or any account
/// identifier is kept: this class exists so the payload can carry
/// <c>account_state = anonymous | signed_in</c> and nothing finer. It never links the
/// analytics <c>distinct_id</c> to a real account; the two identities stay separate by
/// construction.
///
/// The Cloud auth service pushes the state in whenever the session appears or clears;
/// the value is coarse and best-effort (a not-yet-loaded session simply reads
/// <c>anonymous</c> until auth resolves), which is exactly the fidelity product
/// telemetry needs.
/// </summary>
public static class AnalyticsContext
{
    // volatile: written on the auth thread, read on whatever thread builds a payload.
    private static volatile bool _isSignedIn;

    /// <summary>Whether this install is currently signed in to a cloud account. Set by
    /// the Cloud auth layer; read by the analytics payload builder. A plain flag: it
    /// holds no identity.</summary>
    public static bool IsSignedIn
    {
        get => _isSignedIn;
        set => _isSignedIn = value;
    }

    /// <summary>The coarse <c>account_state</c> property value for the current state.</summary>
    public static string AccountState =>
        _isSignedIn ? AnalyticsEvents.AccountStateSignedIn : AnalyticsEvents.AccountStateAnonymous;

    // Backed by device Preferences rather than the app's SQLite settings, and lazily loaded
    // on first read. That is not an optimization: it is what makes the value correct on the
    // FIRST event of a cold launch.
    //
    // app_opened fires from CreateWindow, which by documented design can run before startup
    // has finished (see App.StartAsync). Any restore that waits on the database therefore
    // lands after the session's first event, so every cold start would report `none` and the
    // top of every funnel would be unsegmentable. Preferences is readable synchronously from
    // the payload builder, exactly as AnalyticsIdentity already does for the same reason.
    private const string ReferralKey = "analytics_referral_source";

    // null = not yet loaded from Preferences. volatile: written by the referral layer,
    // read on whatever thread builds a payload.
    private static volatile string? _referralSource;

    /// <summary>Which creator this install came through, or <c>none</c>. Carried on every
    /// event exactly like <see cref="AccountState"/>, so any funnel can be segmented by
    /// channel without a second event stream.
    ///
    /// <para><b>This is a CHANNEL, not a person.</b> It answers "how was Felova found",
    /// which is the same character of fact as the platform or the language, never who the
    /// user is, and never the code they typed (a code is closer to a token than a label).
    /// The privacy boundary above is unchanged: no id, no email, no account identifier, and
    /// events still carry <c>$process_person_profile = false</c>, so nothing here can be
    /// joined into a person profile.</para>
    ///
    /// <para>Deliberately coarse and never null: an unset value reports <c>none</c>, so
    /// "organic" is a filterable bucket rather than a missing property.</para></summary>
    public static string ReferralSource
    {
        get
        {
            if (_referralSource != null)
                return _referralSource;

            // A failed read must not become "no analytics context forever": fall back to
            // none and let a later write correct it.
            try { _referralSource = Preferences.Default.Get(ReferralKey, AnalyticsEvents.ReferralSourceNone); }
            catch { _referralSource = AnalyticsEvents.ReferralSourceNone; }
            return _referralSource;
        }
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? AnalyticsEvents.ReferralSourceNone : value;
            _referralSource = normalized;
            try { Preferences.Default.Set(ReferralKey, normalized); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Analytics] referral persist failed: {ex.Message}"); }
        }
    }

    /// <summary>Forget the channel. Called from the data reset beside
    /// <see cref="AnalyticsIdentity.Rotate"/>: a reset mints a fresh anonymous id so no event
    /// stream runs unbroken across it, and leaving a channel label attached to the new one
    /// would be the one thread still tying the two together.
    ///
    /// <para>Needed explicitly because this lives in <c>Preferences</c>, which the reset's
    /// table sweep does not reach: the SQLite copy in <c>AppSettings</c> goes with
    /// everything else.</para></summary>
    public static void ClearReferral()
    {
        _referralSource = AnalyticsEvents.ReferralSourceNone;
        try { Preferences.Default.Remove(ReferralKey); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Analytics] referral clear failed: {ex.Message}"); }
    }
}
