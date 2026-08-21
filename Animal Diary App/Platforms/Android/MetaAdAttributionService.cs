namespace Animal_Diary_App.Platforms.Android;

using System.Diagnostics;
using Animal_Diary_App.Data.Services.Attribution;
using Com.Facebook;
using Com.Facebook.Appevents;

/// <summary>
/// The only file in the app that names a Meta SDK type. Brings the SDK up far enough to
/// report one install and no further; everything above it sees the plain
/// <see cref="IAdAttributionService"/>.
///
/// <para><b>The install event is not logged here, and there is no call that logs it.</b>
/// <c>AppEventsLogger.ActivateApp</c> hands the SDK the application and the SDK decides:
/// on a first run it reports the install, on later runs it reports an activation. That is
/// the whole integration. Nothing in this app chooses when an install happened, and nothing
/// passes Meta a value of any kind.</para>
///
/// <para><b>The initialization order below is load-bearing.</b> Meta's own source dictates
/// it, and getting it wrong fails in ways that are quiet rather than loud:</para>
///
/// <list type="number">
///   <item><b>Credentials before <c>SdkInitialize</c>.</b> It throws
///   <c>FacebookException</c> outright on a null app id or client token.</item>
///   <item><b><c>SdkInitialize</c> before the three flags.</b>
///   <c>SetAutoLogAppEventsEnabled</c> reaches for the application context internally, and
///   that getter throws if the SDK is not initialized yet.</item>
///   <item><b>The <i>application</i>, never an activity.</b> The SDK only registers the
///   activity-lifecycle tracker that produces activation events when the context it holds
///   is an <c>Application</c>. Hand it an activity and events silently never fire.</item>
/// </list>
///
/// <para><b>Why the SDK cannot start on its own.</b> facebook-core ships a
/// <c>FacebookInitProvider</c> ContentProvider that runs before any app code and calls
/// <c>sdkInitialize</c> unconditionally. It finds no app id (the credentials live in code,
/// not the manifest: see <see cref="MetaAdsConfig"/>), catches its own exception, logs,
/// and gives up. So the opt-out below is genuinely the first thing that decides, rather
/// than racing a provider that already sent the event.</para>
/// </summary>
public sealed class MetaAdAttributionService : IAdAttributionService
{
    private readonly object _gate = new();
    private bool _initialized;

    public bool IsAvailable => MetaAdsConfig.Enabled && MetaAdsConfig.IsConfigured;

    public bool Enabled => IsAvailable && AdTrackingSettings.Enabled;

    public void Start()
    {
        if (!Enabled)
            return;

        EnsureInitialized();
    }

    public void SetEnabled(bool enabled)
    {
        AdTrackingSettings.Enabled = enabled;

        if (!IsAvailable)
            return;

        try
        {
            if (enabled)
            {
                // Covers both cases: never started (initialize now), and started earlier
                // then switched off and back on (flags below are re-applied either way).
                EnsureInitialized();
                return;
            }

            // Switching off. The SDK offers no teardown, so this is as far as an already
            // running process can go: stop logging, stop reading the advertising id. The
            // decision is persisted above, so the next launch never initializes at all.
            if (_initialized)
            {
                FacebookSdk.AutoLogAppEventsEnabled = false;
                FacebookSdk.AdvertiserIDCollectionEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetaAds] could not apply the tracking choice: {ex.Message}");
        }
    }

    private void EnsureInitialized()
    {
        lock (_gate)
        {
            if (_initialized)
            {
                // Already up. Re-apply the flags rather than returning, so switching off
                // and on again inside one process actually resumes logging.
                TryReapplyFlags();
                return;
            }

            try
            {
                var application = global::Android.App.Application.Context
                    as global::Android.App.Application;

                if (application is null)
                {
                    // Nothing to attach the lifecycle tracker to, so initializing would
                    // produce an SDK that never reports anything. Leave it uninitialized
                    // and let the next launch try again.
                    Debug.WriteLine("[MetaAds] no Application context; skipping initialization");
                    return;
                }

                FacebookSdk.ApplicationId = MetaAdsConfig.AppId;
                FacebookSdk.ClientToken = MetaAdsConfig.ClientToken;

#if DEBUG
                // Debug builds only, and the ONLY way to see this subsystem work: without
                // these the SDK is silent, so a device test can't tell "reported the
                // install" from "did nothing". With them, logcat carries the actual POST.
                //
                //     adb logcat -s FacebookSDK.AppEvents:V FacebookSDK:V
                //
                // Set before SdkInitialize so initialization itself is logged. Both are
                // plain flag setters in the SDK and need no initialization of their own.
                // Never enable in Release: it logs event payloads.
                FacebookSdk.IsDebugEnabled = true;
                if (LoggingBehavior.AppEvents is LoggingBehavior appEvents)
                    FacebookSdk.AddLoggingBehavior(appEvents);
#endif

                // Deprecated in the SDK in favour of FullyInitialize(), which is only true
                // for the manifest-driven setup: FullyInitialize sets a flag and nothing
                // else, so with the credentials supplied in code this call is the only
                // thing that actually initializes. (CS0618 is suppressed project-wide.)
                FacebookSdk.SdkInitialize(application);

                FacebookSdk.AutoLogAppEventsEnabled = true;
                FacebookSdk.AdvertiserIDCollectionEnabled = true;
                FacebookSdk.AutoInitEnabled = true;

                AppEventsLogger.ActivateApp(application);

                _initialized = true;
                Debug.WriteLine("[MetaAds] install attribution initialized");
            }
            catch (Exception ex)
            {
                // Fail-safe, same rule as analytics: attribution can never crash or slow
                // the app. A failure here costs one ad campaign some accuracy; a throw on
                // the launch path costs the user their app.
                Debug.WriteLine($"[MetaAds] initialization failed: {ex.Message}");
            }
        }
    }

    private void TryReapplyFlags()
    {
        try
        {
            FacebookSdk.AutoLogAppEventsEnabled = true;
            FacebookSdk.AdvertiserIDCollectionEnabled = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetaAds] could not re-enable logging: {ex.Message}");
        }
    }
}
