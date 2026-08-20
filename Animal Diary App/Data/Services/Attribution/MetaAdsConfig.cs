namespace Animal_Diary_App.Data.Services.Attribution;

/// <summary>
/// Compile-time configuration for Meta install attribution. Mirrors
/// <c>AnalyticsConfig</c> / <c>CloudConfig</c> / <c>BillingConfig</c>: one place holding
/// the master switch and the credentials, so the whole posture is visible in one file.
///
/// <para><b>What this subsystem is for, and what it is not.</b> It reports ONE fact to
/// Meta: this install happened. It exists so a Meta app-promotion campaign can tell which
/// ad produced an install. It is not analytics, it shares no code with the PostHog
/// pipeline, and it must never carry an event, a property, a pet, or anything a person
/// typed. See AI/analytics.md for the boundary in full.</para>
///
/// <para><b>The credentials are not in this file.</b> The Meta App ID and Client Token are
/// supplied by an untracked partial, <c>MetaAdsConfig.Secret.cs</c> (git-ignored), through
/// <see cref="ApplySecrets"/> — the same arrangement as the RevenueCat keys. A fresh clone
/// with no secret file compiles fine (an unimplemented partial method is a legal no-op)
/// and runs credential-less, which registers the null service and sends nothing.</para>
///
/// <para><b>Keeping them out of the AndroidManifest is load-bearing, not just tidy.</b>
/// The Meta SDK's usual setup puts both values in manifest meta-data, where its
/// <c>FacebookInitProvider</c> ContentProvider reads them and initializes the SDK before
/// any app code runs — which would defeat the opt-out entirely, since the first install
/// event would be gone before anything could check a preference. With the values reaching
/// the SDK only through <c>MetaAdAttributionService</c>, that provider finds no app id,
/// logs a failure, and does nothing. The SDK <i>structurally cannot</i> start itself.
/// Do not "helpfully" move these into the manifest.</para>
/// </summary>
public static partial class MetaAdsConfig
{
    /// <summary>
    /// Master switch. False → the null service everywhere, no SDK initialization, no
    /// advertising id read, nothing sent, and the Settings toggle hides itself. This is
    /// the single line to flip to remove Meta from the app's behaviour entirely.
    /// </summary>
    public const bool Enabled = true;

    /// <summary>
    /// True when both credentials are present. Checked separately from
    /// <see cref="Enabled"/> so a clone without the secret file degrades to "off" rather
    /// than initializing an SDK that would throw on a null app id.
    /// </summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ClientToken);

    // ── Secret (untracked) ────────────────────────────────────────────────────
    // Neither value is a server credential: both ship inside the APK by design and can
    // only identify the app to Meta, never read anything back. They are kept out of git
    // per owner preference, exactly like the RevenueCat public SDK keys.

    /// <summary>Meta App ID, from the app's dashboard on developers.facebook.com.</summary>
    public static string AppId { get; private set; } = string.Empty;

    /// <summary>Meta Client Token, from Settings → Advanced in the same dashboard.</summary>
    public static string ClientToken { get; private set; } = string.Empty;

    static MetaAdsConfig() => ApplySecrets();

    /// <summary>Implemented only by the untracked <c>MetaAdsConfig.Secret.cs</c>. With no
    /// implementation this compiles to nothing, so a credential-less build is valid.</summary>
    static partial void ApplySecrets();
}
