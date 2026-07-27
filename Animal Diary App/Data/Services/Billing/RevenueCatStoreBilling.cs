#if ANDROID || IOS
namespace Animal_Diary_App.Data.Services.Billing;

using System.Diagnostics;
using Maui.RevenueCat.InAppBilling.Enums;
using Maui.RevenueCat.InAppBilling.Models;
using Maui.RevenueCat.InAppBilling.Services;

/// <summary>
/// The real store seam, wrapping the community RevenueCat MAUI binding
/// (<c>Kebechet.Maui.RevenueCat.InAppBilling</c>, which bundles the native RevenueCat
/// SDK). This is the ONLY file that touches a RevenueCat type — everything above it
/// sees the plain <see cref="IStoreBilling"/> / <see cref="IEntitlementService"/>.
/// Android/iOS only; the whole file is compiled out elsewhere.
///
/// <para>State is cached so <see cref="IStoreBilling"/>'s synchronous properties
/// (<see cref="HasActiveEntitlement"/>, <see cref="Offers"/>) never block. All binding
/// calls are defensive: a store/network hiccup degrades to "no entitlement / no offers",
/// never a crash — <see cref="EntitlementService"/> wraps too, this is belt-and-braces.</para>
/// </summary>
public sealed class RevenueCatStoreBilling : IStoreBilling
{
    private readonly IRevenueCatBilling _rc;

    private readonly List<SubscriptionOffer> _offers = new();
    private readonly Dictionary<SubscriptionPlan, PackageDto> _packages = new();
    private bool _hasEntitlement;
    private bool _initialized;

    public RevenueCatStoreBilling(IRevenueCatBilling rc) => _rc = rc;

    public bool HasActiveEntitlement => _hasEntitlement;
    public IReadOnlyList<SubscriptionOffer> Offers => _offers;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        var key = ApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return; // No key configured → stays "unavailable"; access can only come from the trial.

        try
        {
            // The binding wants Initialize called after app start, on the main thread.
            await MainThread.InvokeOnMainThreadAsync(() => _rc.Initialize(key));
            _initialized = true;
            await LoadOfferingsAsync();
            await RefreshEntitlementAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] RevenueCat init failed: {ex.Message}");
        }
    }

    public async Task RefreshAsync()
    {
        if (!_initialized)
        {
            await InitializeAsync();
            return;
        }
        await RefreshEntitlementAsync();
    }

    public async Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan)
    {
        if (!_initialized || !_packages.TryGetValue(plan, out var package))
            return PurchaseOutcome.Unavailable;

        try
        {
            var result = await _rc.PurchaseProduct(package);
            if (result.IsSuccess)
            {
                // Trust the returned info, then re-read to be certain the entitlement is live.
                SetEntitlement(IsPremiumActive(result.CustomerInfo));
                await RefreshEntitlementAsync();
                return PurchaseOutcome.Success;
            }

            return result.ErrorStatus == PurchaseErrorStatus.PurchaseCancelledError
                ? PurchaseOutcome.Cancelled
                : PurchaseOutcome.Failed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] purchase failed: {ex.Message}");
            return PurchaseOutcome.Failed;
        }
    }

    public async Task<PurchaseOutcome> RestoreAsync()
    {
        if (!_initialized)
            return PurchaseOutcome.Unavailable;

        try
        {
            var info = await _rc.RestoreTransactions();
            var active = IsPremiumActive(info);
            SetEntitlement(active);
            return active ? PurchaseOutcome.Success : PurchaseOutcome.NothingToRestore;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] restore failed: {ex.Message}");
            return PurchaseOutcome.Failed;
        }
    }

    // ── internals ───────────────────────────────────────────────────────────

    private async Task LoadOfferingsAsync()
    {
        var offerings = await _rc.GetOfferings();
        // Prefer the offering flagged current; fall back to the first available.
        var current = offerings.FirstOrDefault(o => o.IsCurrent) ?? offerings.FirstOrDefault();

        _offers.Clear();
        _packages.Clear();
        if (current is null)
            return;

        foreach (var package in current.AvailablePackages)
        {
            var plan = MapPlan(package.Identifier);
            if (plan is null)
                continue; // ignore weekly/lifetime/etc — we only sell yearly + monthly
            _packages[plan.Value] = package;
            _offers.Add(new SubscriptionOffer(plan.Value, package.Product.Pricing.PriceLocalized, package.Product.Sku));
        }
    }

    private async Task RefreshEntitlementAsync()
    {
        var info = await _rc.GetCustomerInfo();
        SetEntitlement(IsPremiumActive(info));
    }

    /// <summary>Our entitlement is active iff RevenueCat reports the configured
    /// entitlement id as active. (Entitlements are the durable unlock, distinct from the
    /// raw product/subscription ids in <c>ActiveSubscriptions</c>.)</summary>
    private static bool IsPremiumActive(CustomerInfoDto? info) =>
        info?.Entitlements.Any(e => e.Identifier == BillingConfig.EntitlementId && e.IsActive) ?? false;

    private void SetEntitlement(bool value)
    {
        if (value == _hasEntitlement)
            return;
        _hasEntitlement = value;
        Changed?.Invoke();
    }

    /// <summary>Map a RevenueCat package identifier to our two cadences.</summary>
    private static SubscriptionPlan? MapPlan(string packageIdentifier) => packageIdentifier switch
    {
        DefaultPackageIdentifier.Annually => SubscriptionPlan.Yearly,
        DefaultPackageIdentifier.Monthly => SubscriptionPlan.Monthly,
        _ => null,
    };

    private static string ApiKey()
    {
#if ANDROID
        return BillingConfig.AndroidSdkKey;
#elif IOS
        return BillingConfig.IosSdkKey;
#else
        return string.Empty;
#endif
    }
}
#endif
