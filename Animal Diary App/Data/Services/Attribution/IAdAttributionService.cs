namespace Animal_Diary_App.Data.Services.Attribution;

/// <summary>
/// The only ad-attribution API the app uses. Everything injects this and nothing above it
/// names a Meta type: the same arrangement as <c>IAnalyticsService</c> and
/// <c>IInstallReferrerSource</c>. Exactly one implementation touches the SDK
/// (<c>MetaAdAttributionService</c>, Android-only); every other platform and every
/// disabled build gets <see cref="NullAdAttributionService"/>.
///
/// <para>The surface is this small because the job is this small: report that the install
/// happened, and let the owner stop it. There is no <c>Track</c> method by design. Adding
/// one would turn an install counter into a second analytics pipeline pointed at an ad
/// network, which is the thing AI/analytics.md forbids.</para>
/// </summary>
public interface IAdAttributionService
{
    /// <summary>
    /// True when this build and platform can actually report an install: Meta is enabled,
    /// credentials are present, and we are on a platform with an SDK. The Settings toggle
    /// binds its visibility to this, so a switch never appears where it would do nothing.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The owner's current choice. Meaningless when <see cref="IsAvailable"/> is
    /// false, where it always reads false.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Bring the SDK up if the owner allows it. Called once per process from the Android
    /// launch path; a second call is a no-op.
    ///
    /// <para>Must never throw and must never block: it runs on the launch path, and no
    /// failure here is worth a slower or crashed start.</para>
    /// </summary>
    void Start();

    /// <summary>
    /// Record the owner's choice and act on it now.
    ///
    /// <para>Turning it <b>off</b> stops event logging and advertising-id collection
    /// immediately, but cannot un-initialize an SDK already running in this process; the
    /// next launch skips initialization entirely. Turning it <b>on</b> initializes if that
    /// has not happened yet, so it takes effect without a restart.</para>
    /// </summary>
    void SetEnabled(bool enabled);
}
