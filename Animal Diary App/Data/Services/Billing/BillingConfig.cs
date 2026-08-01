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
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(14);

    /// <summary>
    /// DEBUG-ONLY testing switch: run the REAL entitlement gate on Windows/macOS, over a
    /// no-op store. Access then comes from the trial or from caregiver sponsorship — the
    /// two things worth testing on a desktop — while purchases stay unavailable.
    ///
    /// <para>Off by default, and deliberately opt-in rather than "on in Debug": the whole
    /// point of the desktop no-op is that day-to-day development can never be locked out of
    /// the app. Turn it on only while testing gating, and remember a desktop build cannot
    /// buy anything, so the only way back out of the read-only state there is to turn this
    /// off again or move the trial anchor.</para>
    ///
    /// <para>Why it exists: on desktop the gate is <c>NullEntitlementService</c>, which
    /// reports <c>Subscribed</c> for every account unconditionally. A caregiver test run
    /// with a desktop as the second device therefore passes every gate check without
    /// exercising a single one.</para>
    /// </summary>
    public const bool ForceGateOnDesktop = false;

    /// <summary>Show the pre-end nudge once when the trial has this many days left.</summary>
    public const int PreEndNudgeDaysBefore = 3;

    /// <summary>How long a cached sponsorship keeps working without reaching the server.
    /// A caregiver at the vet with no signal must still be able to log what just happened,
    /// so this is deliberately generous.
    ///
    /// <para>This bounds ONLY the "owner's subscription lapsed" case. Losing <i>membership</i>
    /// (removed, or you left) is bounded by sync instead: the membership diff purges the pet
    /// and all its data from the device outright. Two different bounds because they answer
    /// two different questions — billing vs. privacy. Do not unify them.</para></summary>
    public static readonly TimeSpan SponsorshipOfflineGrace = TimeSpan.FromDays(14);

    /// <summary>Max caregivers sharing one pet, and max sponsored caregivers one owner may
    /// have across all their pets. Mirrored in migration 0010 — the server is the real
    /// enforcement; these exist so the client can explain the limit in the owner's language.
    /// Never applied retroactively: an owner already over the cap keeps everyone.</summary>
    public const int MaxCaregiversPerPet = 5;
    public const int MaxSponsoredCaregiversPerOwner = 10;

    /// <summary>RevenueCat entitlement identifier that unlocks full access. Must match the
    /// identifier in the RevenueCat dashboard EXACTLY (it is "Felova Full", spaces and
    /// all). As a safety net the check also treats any active entitlement as full access,
    /// since this app has a single paid tier — see RevenueCatStoreBilling.IsPremiumActive.</summary>
    public const string EntitlementId = "Felova Full";

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
