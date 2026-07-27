namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// Monetization configuration. The tunable knobs (trial length, entitlement/offering
/// ids) live here as single values so they are painless to change. The RevenueCat
/// public SDK key is NOT in this committed file: it is supplied by an untracked
/// partial, <c>BillingConfig.Secret.cs</c> (git-ignored), via <see cref="ApplySecrets"/>.
///
/// <para>The class compiles with or without that secret file — a missing partial
/// method body is a legal no-op, so a fresh clone builds and simply runs key-less
/// (which registers <c>NullEntitlementService</c> and never locks). Drop the real key
/// in later without touching this file.</para>
///
/// <para><b>Enable</b> is an explicit switch, deliberately not derived from key
/// presence: flip it to <c>true</c> only once the key AND the RevenueCat binding are
/// both wired (slice 1), so the read-only gate can never go live before purchases work.</para>
/// </summary>
public static partial class BillingConfig
{
    /// <summary>Master switch. False → <c>NullEntitlementService</c> everywhere, zero
    /// monetization behaviour. True (with the RevenueCat binding in and a key present)
    /// turns the trial + read-only gate live on Android/iOS. Windows/macOS dev stays on
    /// the no-op regardless. Currently ON, running against the RevenueCat Test Store key.</summary>
    public const bool Enabled = true;

    /// <summary>The free-trial length. One editable value — tune freely (owner will).</summary>
    public static readonly TimeSpan TrialLength = TimeSpan.FromMinutes(2);

    /// <summary>Show the pre-end nudge once when the trial has this many days left.</summary>
    public const int PreEndNudgeDaysBefore = 3;

    /// <summary>RevenueCat entitlement identifier that unlocks full access.</summary>
    public const string EntitlementId = "premium";

    /// <summary>RevenueCat offering identifier holding the yearly + monthly packages.</summary>
    public const string OfferingId = "default";

    // ── Secret (untracked) ────────────────────────────────────────────────────
    // The RevenueCat *public* SDK keys. Publishable by nature, but kept out of git per
    // owner preference. Populated by BillingConfig.Secret.cs when present; empty here.

    /// <summary>RevenueCat public SDK key for the Google Play app (goog_…).</summary>
    public static string AndroidSdkKey { get; private set; } = string.Empty;

    /// <summary>RevenueCat public SDK key for the App Store app (appl_…).</summary>
    public static string IosSdkKey { get; private set; } = string.Empty;

    static BillingConfig() => ApplySecrets();

    /// <summary>Implemented only by the untracked <c>BillingConfig.Secret.cs</c>. With no
    /// implementation this compiles to nothing, so the key-less build is valid.</summary>
    static partial void ApplySecrets();
}
