namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The app's single monetization boundary. Every feature asks <b>this</b> whether
/// the user may add/edit — never RevenueCat directly — exactly like features talk to
/// <see cref="Analytics.IAnalyticsService"/> or the cloud boundary. That indirection
/// is what makes the trial <i>reversible</i>: today access comes from a no-card,
/// app-side trial OR a RevenueCat subscription; switching to a store-native
/// (card-up-front) trial later is a change behind this interface only, because the
/// <see cref="HasFullAccess"/> formula never moves out of here.
///
/// <para>The single source of truth is:
/// <c>HasFullAccess = trial-still-running OR subscription-entitlement-active</c>.
/// Read it; do not re-derive it from <see cref="State"/> or the trial clock.</para>
///
/// Contract for every implementation:
/// <list type="bullet">
///   <item>Never throws from a property. Purchase/restore surface an outcome, not an
///   exception, to the caller.</item>
///   <item>When billing is disabled or the platform has no store (Windows/macOS dev),
///   <c>NullEntitlementService</c> is registered and <see cref="HasFullAccess"/> is
///   always true — the app is never locked in development.</item>
/// </list>
/// </summary>
public interface IEntitlementService
{
    /// <summary>The one gate every add/edit surface checks. True while the trial is
    /// running or a subscription is active; false in the care-only read state.</summary>
    bool HasFullAccess { get; }

    /// <summary>Coarse state for copy/telemetry only — <b>not</b> the gate.
    /// <see cref="HasFullAccess"/> is the gate.</summary>
    AccessState State { get; }

    /// <summary>Whole days left in the app-side trial (0 once expired or subscribed).
    /// Drives the pre-end nudge copy; never a live countdown UI.</summary>
    int TrialDaysLeft { get; }

    /// <summary>Exact time left in the app-side trial (<see cref="TimeSpan.Zero"/> once
    /// expired / subscribed / not started). Lets the UI show "3 days left" or, near the
    /// end, "45 minutes left". Read on demand — it is not a ticking clock.</summary>
    TimeSpan TrialTimeRemaining { get; }

    /// <summary>The store's subscription offers to show on the subscribe sheet
    /// (yearly first). Empty until <see cref="InitializeAsync"/> has run, when the
    /// store is unreachable, or under the Null implementation.</summary>
    IReadOnlyList<SubscriptionOffer> Offers { get; }

    /// <summary>Raised whenever <see cref="HasFullAccess"/> or <see cref="State"/> may
    /// have changed (trial elapsed, purchase, restore, expiry). Marshal to the UI
    /// thread before touching bindings.</summary>
    event Action? StateChanged;

    /// <summary>Wire up the store and load persisted trial state. Safe to call once at
    /// startup, off the UI path; idempotent and non-throwing.</summary>
    Task InitializeAsync();

    /// <summary>Idempotently start the trial clock if it has not started yet. Called
    /// when the app enters the main experience with at least one pet (onboarding
    /// completion, and for already-onboarded users on first launch of this build).
    /// Returns true only on the call that actually begins the trial, so the caller can
    /// fire <c>trial_started</c> exactly once.</summary>
    Task<bool> EnsureTrialStartedAsync();

    /// <summary>Re-check the entitlement with the store (app resume / after a purchase
    /// elsewhere). Non-throwing.</summary>
    Task RefreshAsync();

    /// <summary>Re-fetch the purchasable <see cref="Offers"/> (called when the subscribe
    /// sheet opens, so a slow/failed initial load recovers). Non-throwing.</summary>
    Task RefreshOffersAsync();

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

/// <summary>Coarse access state, for copy selection and analytics only.</summary>
public enum AccessState
{
    /// <summary>Before <see cref="IEntitlementService.InitializeAsync"/> completes.</summary>
    Unknown,
    /// <summary>Inside the free trial window.</summary>
    Trial,
    /// <summary>Trial elapsed (or a subscription lapsed) and not currently subscribed
    /// — the care-only read state.</summary>
    TrialExpired,
    /// <summary>An active paid subscription.</summary>
    Subscribed
}

/// <summary>The two subscription cadences. Yearly is the emphasized/default option.</summary>
public enum SubscriptionPlan
{
    Yearly,
    Monthly
}

/// <summary>The result of a purchase/restore attempt — never an exception to the UI.</summary>
public enum PurchaseOutcome
{
    /// <summary>Bought/restored; the entitlement is now active.</summary>
    Success,
    /// <summary>The user backed out of the store sheet. Not an error.</summary>
    Cancelled,
    /// <summary>No active subscription was found to restore.</summary>
    NothingToRestore,
    /// <summary>The payment is deferred (slow card, family approval) or the entitlement
    /// hasn't propagated yet. Not a failure — it may complete shortly and unlock then.</summary>
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
/// <param name="PriceLabel">Store-formatted recurring price, e.g. "€3.99" / "$24.99".</param>
/// <param name="StoreProductId">The underlying store product id, for telemetry only.</param>
public sealed record SubscriptionOffer(SubscriptionPlan Plan, string PriceLabel, string StoreProductId);
