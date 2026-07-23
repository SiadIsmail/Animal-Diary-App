namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// The one piece of coarse, ambient context the payload builder needs but can't read
/// from itself: whether this install is currently signed in to cloud. It is a tiny
/// static flag — deliberately NOT an injected dependency — so the analytics subsystem
/// stays free of any Cloud type and the Cloud subsystem stays free of analytics beyond
/// its <see cref="IAnalyticsService"/> calls (the two worlds never reference each
/// other's classes, mirroring how <see cref="AnalyticsIdentity"/> is a leaf static).
///
/// <b>Privacy boundary (hard rule):</b> the only thing that may ever be stored here is a
/// boolean signed-in/anonymous state. No user id, email, token, or any account
/// identifier is kept — this class exists so the payload can carry
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
    /// the Cloud auth layer; read by the analytics payload builder. A plain flag — it
    /// holds no identity.</summary>
    public static bool IsSignedIn
    {
        get => _isSignedIn;
        set => _isSignedIn = value;
    }

    /// <summary>The coarse <c>account_state</c> property value for the current state.</summary>
    public static string AccountState =>
        _isSignedIn ? AnalyticsEvents.AccountStateSignedIn : AnalyticsEvents.AccountStateAnonymous;
}
