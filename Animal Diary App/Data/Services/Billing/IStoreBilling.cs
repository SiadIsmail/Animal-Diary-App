namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The thin seam over the native store / RevenueCat SDK. <b>All</b> RevenueCat
/// awareness lives behind this — no RevenueCat type escapes the Billing folder, the
/// same rule the cloud boundary follows for Supabase. <see cref="EntitlementService"/>
/// composes this with the app-side <see cref="TrialService"/>; swapping the real
/// implementation in is the only place a purchase actually happens.
///
/// <para>Until the RevenueCat MAUI binding is chosen and wired (the slice-1 spike),
/// <see cref="NullStoreBilling"/> stands in: no entitlement, no offers, purchases
/// report <see cref="PurchaseOutcome.Unavailable"/>. That keeps the whole app,
/// including the trial and the read-only gate, compiling and runnable today.</para>
/// </summary>
public interface IStoreBilling
{
    /// <summary>True when the store reports an active subscription entitlement.</summary>
    bool HasActiveEntitlement { get; }

    /// <summary>The purchasable offers, yearly first. Empty until initialized / when
    /// the store is unreachable.</summary>
    IReadOnlyList<SubscriptionOffer> Offers { get; }

    /// <summary>Raised when the entitlement or offers change (purchase, restore,
    /// store push). May fire on a background thread.</summary>
    event Action? Changed;

    /// <summary>Configure the SDK and fetch offerings + current entitlement.
    /// Non-throwing; a failure leaves <see cref="HasActiveEntitlement"/> false.</summary>
    Task InitializeAsync();

    /// <summary>Re-fetch the current entitlement (app resume / post-purchase).</summary>
    Task RefreshAsync();

    Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan);

    Task<PurchaseOutcome> RestoreAsync();
}

/// <summary>
/// No-store stand-in. Registered on Windows/macOS (no billing) and everywhere until
/// the RevenueCat binding is wired. Reports no subscription and refuses purchases, so
/// access can only ever come from the app-side trial while this is in place.
/// </summary>
public sealed class NullStoreBilling : IStoreBilling
{
    public bool HasActiveEntitlement => false;
    public IReadOnlyList<SubscriptionOffer> Offers => Array.Empty<SubscriptionOffer>();

#pragma warning disable CS0067 // Never raised: nothing changes under the null store.
    public event Action? Changed;
#pragma warning restore CS0067

    public Task InitializeAsync() => Task.CompletedTask;
    public Task RefreshAsync() => Task.CompletedTask;
    public Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan) => Task.FromResult(PurchaseOutcome.Unavailable);
    public Task<PurchaseOutcome> RestoreAsync() => Task.FromResult(PurchaseOutcome.Unavailable);
}
