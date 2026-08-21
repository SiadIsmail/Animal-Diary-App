namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The app's single monetization boundary. Every feature asks <b>this</b> whether the
/// user has the paid tier (never RevenueCat directly) exactly like features talk to
/// <see cref="Analytics.IAnalyticsService"/> or the cloud boundary.
///
/// <para><b>What this gate is, and what it is emphatically not.</b> Writing things down
/// is free forever: logging, medications, the care plan, the dose loop, reading, and
/// getting your data out are never gated, in any state, on any tier. The safety net is
/// the free product. What this boundary sells is the things the accumulated record is
/// <i>for</i>: the assembled appointment summary, the designed vet report, a second
/// pet, cloud backup, minting a caregiver invite. Never reintroduce a check on a logging
/// path: AI/README.md carries that as a non-negotiable rule.</para>
///
/// <para>The single source of truth is:
/// <c>HasFullAccess = subscription-entitlement-active OR access-code-grant-running</c>.
/// Read it; do not re-derive it from <see cref="State"/>.</para>
///
/// Contract for every implementation:
/// <list type="bullet">
///   <item>Never throws from a property. Purchase/restore surface an outcome, not an
///   exception, to the caller.</item>
///   <item>When billing is disabled or the platform has no store (Windows/macOS dev),
///   <c>NullEntitlementService</c> is registered and <see cref="HasFullAccess"/> is
///   always true: the app is never gated in development.</item>
/// </list>
/// </summary>
public interface IEntitlementService
{
    /// <summary><b>Your own</b> paid access: an active subscription or a running grant.
    /// False on the permanent free tier. This is the gate for the paid surfaces that are
    /// yours alone: adding a second pet, minting an invite, turning on cloud backup, the
    /// designed report, the second appointment summary. For anything scoped to a
    /// particular pet use <see cref="CanEditPet"/>, which also honours sponsorship.</summary>
    bool HasFullAccess { get; }

    /// <summary>Whether the paid, pet-scoped surfaces are available for ONE pet. True when
    /// you have your own paid access, <b>or</b> you are a caregiver on this pet and its
    /// owner does: the sponsorship rule:
    ///
    /// <para><c>CanEditPet = HasFullAccess || (I am a caregiver here &amp;&amp; the owner has access)</c></para>
    ///
    /// <para><b>This is no longer a write gate.</b> Every write (journal entries,
    /// medications, the care plan, the pet profile) is free on every tier, so nothing in
    /// the logging path may call this. It survives because the paid, pet-scoped surfaces
    /// (the assembled summary, the designed report) need exactly this question answered,
    /// sponsorship included.</para>
    ///
    /// Sponsorship never reaches your own pets, which is what stops one subscription from
    /// becoming unlimited free accounts. Pure, synchronous and local-first: it short-circuits
    /// on <see cref="HasFullAccess"/> before looking at any cloud state, so a signed-out
    /// subscriber is never affected by it. Never throws.</summary>
    /// <param name="petSyncId">The pet's <c>SyncId</c>. Null/empty is treated as "not
    /// shared", so the answer collapses to <see cref="HasFullAccess"/>.</param>
    bool CanEditPet(string? petSyncId);

    /// <summary>Coarse state for copy/telemetry only: <b>not</b> the gate.
    /// <see cref="HasFullAccess"/> is the gate.</summary>
    AccessState State { get; }

    /// <summary>When a redeemed access code's grant ends, or null when none is running.
    /// Copy only. See <see cref="IGrantSource"/> for what a grant is and is not.</summary>
    DateTime? GrantedUntilUtc { get; }

    /// <summary>Whether this account has ever held a grant, including an expired one. It is
    /// the guard that selects grant copy: someone whose redeemed year ran out has not
    /// cancelled anything and must never be addressed as though they had. A grant is not a
    /// subscription: nothing was charged, nothing renewed, there was nothing to cancel.</summary>
    bool EverGranted { get; }

    /// <summary>The store's subscription offers to show on the subscribe sheet
    /// (yearly first). Empty until <see cref="InitializeAsync"/> has run, when the
    /// store is unreachable, or under the Null implementation.</summary>
    IReadOnlyList<SubscriptionOffer> Offers { get; }

    /// <summary>Raised whenever <see cref="HasFullAccess"/> or <see cref="State"/> may
    /// have changed (purchase, restore, expiry, a redeemed code). Marshal to the UI
    /// thread before touching bindings.</summary>
    event Action? StateChanged;

    /// <summary>Wire up the store and load persisted access state. Safe to call once at
    /// startup, off the UI path; idempotent and non-throwing.</summary>
    Task InitializeAsync();

    /// <summary>Re-check the entitlement with the store (app resume / after a purchase
    /// elsewhere). Non-throwing.</summary>
    Task RefreshAsync();

    /// <summary>Tie the subscription to the signed-in account (null on sign-out), so it
    /// follows the person across devices. Called from the cloud auth session hook. An
    /// account is never required to buy or to keep access. Non-throwing.</summary>
    Task IdentifyAsync(string? accountId);

    /// <summary>Re-fetch the purchasable <see cref="Offers"/> (called when the subscribe
    /// sheet opens, so a slow/failed initial load recovers). Non-throwing.</summary>
    Task RefreshOffersAsync();

    /// <summary>Tag the store identity with a creator code, so a purchase made later carries
    /// it (see <see cref="IStoreBilling.SetAttributionAsync"/>). Routed through this boundary
    /// only because the store seam lives behind it: attribution grants nothing and no gate
    /// reads it. Non-throwing.</summary>
    Task SetAttributionAsync(string? creatorCode);

    /// <summary>Buy one of the <see cref="Offers"/>. Routes through native store
    /// billing; returns an outcome rather than throwing.</summary>
    Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan);

    /// <summary>Restore a subscription bought on another device / after reinstall.
    /// Required by the stores; returns an outcome rather than throwing.</summary>
    Task<PurchaseOutcome> RestoreAsync();

    /// <summary>The store's "manage / change / cancel this subscription" URL for the
    /// current platform (Play or App Store), or null if unavailable. Non-throwing.</summary>
    Task<string?> GetManagementUrlAsync();
}

/// <summary>Coarse access state, for copy selection and analytics only.
///
/// <para><b>Every consumer must name every member.</b> The Settings subtitle in particular
/// selects copy by switch, so a state that falls through to a default arm is described to
/// the user as something it is not. Adding OR removing a member here means visiting all of
/// them, not just the ones that stop compiling.</para></summary>
public enum AccessState
{
    /// <summary>Before <see cref="IEntitlementService.InitializeAsync"/> completes.</summary>
    Unknown,
    /// <summary>The permanent free tier. Not a lapse, not an expiry, and not a countdown:
    /// everything the owner has written down stays readable, exportable and <b>writable</b>
    /// here, forever. Copy must never describe this state as ended, over, or running out.</summary>
    Free,
    /// <summary>Full access from a redeemed access code, running until
    /// <see cref="IEntitlementService.GrantedUntilUtc"/>. Never call this "subscribed" in
    /// copy: nothing was charged, nothing renews, and there is nothing to cancel.</summary>
    Granted,
    /// <summary>An active paid subscription.</summary>
    Subscribed
}

/// <summary>The two subscription cadences. Yearly is the emphasized/default option.</summary>
public enum SubscriptionPlan
{
    Yearly,
    Monthly
}

/// <summary>The result of a purchase/restore attempt, never an exception to the UI.</summary>
public enum PurchaseOutcome
{
    /// <summary>Bought/restored; the entitlement is now active.</summary>
    Success,
    /// <summary>Nothing was bought because this store account already owns a subscription,
    /// and it belongs to <b>this</b> app account, so access is now active. Distinct from
    /// <see cref="Success"/> because telling someone "you subscribed" when they didn't is a
    /// small lie that reads as a double charge. Reachable after a reinstall, or on a second
    /// device sharing the store account.</summary>
    AlreadySubscribed,
    /// <summary>This store account already owns a subscription, but it is attached to a
    /// DIFFERENT app account, so it cannot be granted here. The store will not sell a second
    /// one, so the only ways forward are signing in as the account that holds it or managing
    /// it in the store: say that, rather than reporting a failure they cannot act on.</summary>
    OwnedByAnotherAccount,
    /// <summary>The user backed out of the store sheet. Not an error.</summary>
    Cancelled,
    /// <summary>No active subscription was found to restore.</summary>
    NothingToRestore,
    /// <summary>The payment is deferred (slow card, family approval) or the entitlement
    /// hasn't propagated yet. Not a failure: it may complete shortly and unlock then.</summary>
    Pending,
    /// <summary>Billing is unavailable (no store, offline, misconfigured).</summary>
    Unavailable,
    /// <summary>The store reported a failure.</summary>
    Failed
}

/// <summary>One purchasable subscription, with a store-localized price string. The
/// price text comes from the store (never hardcoded), so currency and formatting are
/// always correct for the user's region.</summary>
/// <param name="Plan">Which cadence this offer is.</param>
/// <param name="PriceLabel">Store-formatted recurring price, e.g. "€35.00" / "$39.99".</param>
/// <param name="StoreProductId">The underlying store product id, for telemetry only.</param>
public sealed record SubscriptionOffer(SubscriptionPlan Plan, string PriceLabel, string StoreProductId);
