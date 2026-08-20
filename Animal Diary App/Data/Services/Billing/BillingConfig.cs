namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// Monetization configuration. The tunable knobs (entitlement/offering ids, the
/// sponsorship grace, the caregiver caps) live here as single values so they are painless
/// to change. Prices are NOT among them and never will be: they come from RevenueCat
/// <c>Offerings</c>, store-localized. The RevenueCat
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
/// both wired, so a paid surface can never be withheld before purchases work.</para>
/// </summary>
public static partial class BillingConfig
{
    /// <summary>Master switch. False → <c>NullEntitlementService</c> everywhere, zero
    /// monetization behaviour. True (with the RevenueCat binding in and a key present)
    /// turns the paid tier live on Android/iOS. Windows/macOS dev stays on the no-op
    /// regardless. Currently ON, running against the RevenueCat Test Store key.</summary>
    public const bool Enabled = true;

    /// <summary>
    /// DEBUG-ONLY testing switch: run the REAL entitlement gate on Windows/macOS, over a
    /// no-op store. Access then comes from caregiver sponsorship or a redeemed code — the
    /// two things worth testing on a desktop — while purchases stay unavailable.
    ///
    /// <para>Off by default, and deliberately opt-in rather than "on in Debug": the whole
    /// point of the desktop no-op is that day-to-day development never sees a paid surface
    /// withheld. Turn it on only while testing the boundary, and remember a desktop build
    /// cannot buy anything, so the only way back out is to turn it off again.</para>
    ///
    /// <para>Why it exists: on desktop the gate is <c>NullEntitlementService</c>, which
    /// reports <c>Subscribed</c> for every account unconditionally. A caregiver test run
    /// with a desktop as the second device therefore passes every gate check without
    /// exercising a single one.</para>
    /// </summary>
    public const bool ForceGateOnDesktop = false;

    /// <summary>Show the one heads-up this many days before a redeemed access code's grant
    /// runs out. A grant is the only thing left in the app with an end date — there is no
    /// trial and the free tier never ends — so this window has exactly one reader.</summary>
    public const int GrantEndingNoticeDaysBefore = 3;

    /// <summary>How long a cached sponsorship keeps working without reaching the server.
    /// A caregiver at the vet with no signal must still reach the pet's summary, so this is
    /// deliberately generous. It has never bounded logging and no longer could: writing
    /// things down is free on every tier.
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
