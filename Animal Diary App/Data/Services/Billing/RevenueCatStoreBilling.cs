#if ANDROID || IOS
namespace Animal_Diary_App.Data.Services.Billing;

using System.Diagnostics;
using Maui.RevenueCat.InAppBilling.Enums;
using Maui.RevenueCat.InAppBilling.Models;
using Maui.RevenueCat.InAppBilling.Services;

/// <summary>
/// The real store seam, wrapping the community RevenueCat MAUI binding
/// (<c>Kebechet.Maui.RevenueCat.InAppBilling</c>, which bundles the native RevenueCat
/// SDK). This is the ONLY file that touches a RevenueCat type: everything above it
/// sees the plain <see cref="IStoreBilling"/> / <see cref="IEntitlementService"/>.
/// Android/iOS only; the whole file is compiled out elsewhere.
///
/// <para>State is cached in immutable snapshots so <see cref="IStoreBilling"/>'s
/// synchronous properties (<see cref="HasActiveEntitlement"/>, <see cref="Offers"/>)
/// never block and are safe to read from the UI thread while a background refresh
/// mutates. Every binding call is defensive and time-bounded: a store/network hiccup
/// degrades to "no entitlement / no offers", never a crash or a hung spinner.</para>
/// </summary>
public sealed class RevenueCatStoreBilling : IStoreBilling
{
    // Upper bound on any single background store call so a stalled network can't hang the
    // subscribe sheet's spinner (the SDK has internal timeouts; this guarantees UI recovery).
    private const int StoreCallTimeoutSeconds = 15;

    private readonly IRevenueCatBilling _rc;

    // Immutable snapshots, swapped by reference so readers never see a torn/mutating list.
    private volatile SubscriptionOffer[] _offers = Array.Empty<SubscriptionOffer>();
    private volatile Dictionary<SubscriptionPlan, PackageDto> _packages = new();

    // Serializes offerings loads (init-time vs sheet-open) so they don't interleave.
    private readonly SemaphoreSlim _offersGate = new(1, 1);

    // configure-once: cached so concurrent callers await the same operation.
    private readonly object _initLock = new();
    private Task? _initTask;

    private volatile bool _configured;   // _rc.Initialize succeeded
    private volatile bool _entitlementKnown;
    private bool _hasEntitlement;

    public RevenueCatStoreBilling(IRevenueCatBilling rc) => _rc = rc;

    public bool HasActiveEntitlement => _hasEntitlement;
    public bool EntitlementKnown => _entitlementKnown;
    public IReadOnlyList<SubscriptionOffer> Offers => _offers;

    public event Action? Changed;

    public Task InitializeAsync()
    {
        lock (_initLock)
            return _initTask ??= InitCoreAsync();
    }

    private async Task InitCoreAsync()
    {
        var key = ApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return; // No key configured → stays "unavailable"; only a redeemed code grants access.

        try
        {
            // The binding wants Initialize called after app start, on the main thread.
            await MainThread.InvokeOnMainThreadAsync(() => _rc.Initialize(key));
            _configured = true;
            // The RevenueCat customer id: use it to find this device in the dashboard's
            // Customers list (e.g. to cancel a Test Store subscription while testing).
            Debug.WriteLine($"[Billing] RevenueCat app user id: {_rc.GetAppUserId()} (anonymous={_rc.IsAnonymous()})");
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
        if (!_configured)
        {
            await InitializeAsync();
            return;
        }
        await RefreshEntitlementAsync();
    }

    public async Task RefreshOffersAsync()
    {
        if (!_configured)
        {
            // Not configured yet (init still running or failed): running init also loads
            // the offerings (and InitializeAsync is cached, so this is cheap/idempotent).
            await InitializeAsync();
            return;
        }
        try { await LoadOfferingsAsync(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] offers refresh failed: {ex.Message}"); }
        Changed?.Invoke();
    }

    public async Task<PurchaseOutcome> PurchaseAsync(SubscriptionPlan plan)
    {
        var package = PackageFor(plan);
        if (!_configured || package is null)
            return PurchaseOutcome.Unavailable;

        try
        {
            // Note: the purchase itself is NOT time-bounded: the user may be entering card
            // details in the store sheet; only our background fetches are.
            var result = await _rc.PurchaseProduct(package);
            if (result.IsSuccess)
            {
                LogCustomerInfo("purchase result", result.CustomerInfo);
                SetEntitlement(IsPremiumActive(result.CustomerInfo));
                await RefreshEntitlementAsync();
                if (_hasEntitlement)
                    return PurchaseOutcome.Success;

                // The money went through but no active entitlement yet: either propagation
                // lag, or a dashboard misconfig (the entitlement isn't attached to this
                // product, or Sandbox Testing Access is restricting grants). It is NEVER a
                // failure: surface it as pending and log the config hint.
                Debug.WriteLine(
                    $"[Billing] purchase succeeded but entitlement '{BillingConfig.EntitlementId}' " +
                    "is not active yet. If this persists, check the RevenueCat dashboard: an " +
                    "entitlement with this id exists, is attached to the product, and Sandbox " +
                    "Testing Access grants it.");
                return PurchaseOutcome.Pending;
            }

            return result.ErrorStatus switch
            {
                PurchaseErrorStatus.PurchaseCancelledError => PurchaseOutcome.Cancelled,
                // Deferred payment (slow card, family approval): may complete later.
                PurchaseErrorStatus.PaymentPendingError => PurchaseOutcome.Pending,
                // Already owned on this store account → work out whether it is ours.
                PurchaseErrorStatus.ProductAlreadyPurchasedError => await ResolveAlreadyOwnedAsync(),
                // The store account's receipt is held by a different app account. Not a
                // failure they can retry: the store will never sell them a second one.
                PurchaseErrorStatus.ReceiptAlreadyInUseError
                    or PurchaseErrorStatus.ReceiptInUseByOtherSubscriberError
                    or PurchaseErrorStatus.PurchaseBelongsToOtherUser => PurchaseOutcome.OwnedByAnotherAccount,
                _ => PurchaseOutcome.Failed,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] purchase failed: {ex.Message}");
            return PurchaseOutcome.Failed;
        }
    }

    /// <summary>
    /// The store refused the sale because this store account already owns the product. Two
    /// very different situations hide behind that one error, and they need opposite messages:
    ///
    /// <list type="bullet">
    ///   <item><b>It's ours</b>: reinstall, or a second device on the same store account.
    ///   The entitlement resolves and access is already on: <see cref="PurchaseOutcome.AlreadySubscribed"/>.</item>
    ///   <item><b>It belongs to another app account</b>: the store account bought it while
    ///   signed in as someone else, and RevenueCat is configured to keep purchases with the
    ///   account that made them. Nothing we can do here grants it, and the store will not
    ///   sell a second one, so saying "pending" or "failed" strands them:
    ///   <see cref="PurchaseOutcome.OwnedByAnotherAccount"/>.</item>
    /// </list>
    /// </summary>
    private async Task<PurchaseOutcome> ResolveAlreadyOwnedAsync()
    {
        await RefreshEntitlementAsync();
        if (_hasEntitlement)
            return PurchaseOutcome.AlreadySubscribed;

        try
        {
            var info = await WithTimeout(_rc.RestoreTransactions(), StoreCallTimeoutSeconds, (CustomerInfoDto?)null);
            LogCustomerInfo("already-owned restore", info);
            if (info is not null)
            {
                SetEntitlement(IsPremiumActive(info));
                _entitlementKnown = true;
            }
        }
        catch (Exception ex)
        {
            // The restore itself reports "this receipt is another user's" as an exception.
            // That is the confirmation, not a failure: it tells us which of the two cases
            // above we are in.
            Debug.WriteLine($"[Billing] already-owned restore failed: {ex.Message}");
            if (IsOwnedByAnotherAccount(ex))
                return PurchaseOutcome.OwnedByAnotherAccount;
        }

        if (_hasEntitlement)
            return PurchaseOutcome.AlreadySubscribed;

        // Owned by the store account, not grantable here, and the restore did not say why.
        // Still the honest answer: they cannot buy it and cannot use it on this account.
        Debug.WriteLine(
            "[Billing] product already owned by this store account but no entitlement resolved: " +
            "most likely held by a different app account (RevenueCat transfer behaviour is " +
            "'keep with original App User ID').");
        return PurchaseOutcome.OwnedByAnotherAccount;
    }

    /// <summary>Does this error mean "the store account owns it, but under a different app
    /// account"? These are the codes RevenueCat raises when transfer behaviour keeps a
    /// purchase with the account that made it.</summary>
    private static bool IsOwnedByAnotherAccount(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
        return text.Contains(nameof(PurchaseErrorStatus.ReceiptAlreadyInUseError), StringComparison.OrdinalIgnoreCase)
            || text.Contains(nameof(PurchaseErrorStatus.ReceiptInUseByOtherSubscriberError), StringComparison.OrdinalIgnoreCase)
            || text.Contains(nameof(PurchaseErrorStatus.PurchaseBelongsToOtherUser), StringComparison.OrdinalIgnoreCase)
            || text.Contains("already in use", StringComparison.OrdinalIgnoreCase)
            || text.Contains("belongs to", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PurchaseOutcome> RestoreAsync()
    {
        if (!_configured)
            return PurchaseOutcome.Unavailable;

        try
        {
            var info = await WithTimeout(_rc.RestoreTransactions(), StoreCallTimeoutSeconds, (CustomerInfoDto?)null);
            LogCustomerInfo("restore result", info);
            if (info is not null)
                _entitlementKnown = true;
            var active = IsPremiumActive(info);
            SetEntitlement(active);
            return active ? PurchaseOutcome.Success : PurchaseOutcome.NothingToRestore;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] restore failed: {ex.Message}");
            // "Restore" on a store account whose purchase belongs to another app account is
            // the single most likely way someone meets this state: they try Restore first.
            return IsOwnedByAnotherAccount(ex)
                ? PurchaseOutcome.OwnedByAnotherAccount
                : PurchaseOutcome.Failed;
        }
    }

    public async Task<string?> GetManagementUrlAsync()
    {
        if (!_configured)
            return null;
        try { return await _rc.GetManagementSubscriptionUrl(); }
        catch (Exception ex) { Debug.WriteLine($"[Billing] management url failed: {ex.Message}"); return null; }
    }

    // ── internals ───────────────────────────────────────────────────────────

    private PackageDto? PackageFor(SubscriptionPlan plan)
        => _packages.TryGetValue(plan, out var package) ? package : null;

    private async Task LoadOfferingsAsync()
    {
        await _offersGate.WaitAsync();
        try
        {
            // On a timeout/failure we keep whatever we already had (build into locals and
            // only swap on success) rather than wiping a good list.
            var offerings = await WithTimeout(_rc.GetOfferings(), StoreCallTimeoutSeconds, new List<OfferingDto>());
            // Prefer the offering flagged current; fall back to the first available.
            var current = offerings.FirstOrDefault(o => o.IsCurrent) ?? offerings.FirstOrDefault();
            if (current is null)
                return;

            var offers = new List<SubscriptionOffer>();
            var packages = new Dictionary<SubscriptionPlan, PackageDto>();
            foreach (var package in current.AvailablePackages)
            {
                var plan = MapPlan(package.Identifier);
                if (plan is null)
                    continue; // ignore weekly/lifetime/etc: we only sell yearly + monthly
                packages[plan.Value] = package;
                offers.Add(new SubscriptionOffer(plan.Value, package.Product.Pricing.PriceLocalized, package.Product.Sku));
            }

            // Atomic snapshot swap: readers see either the old or the new list, never a
            // half-built one.
            _offers = offers.ToArray();
            _packages = packages;
        }
        finally
        {
            _offersGate.Release();
        }
    }

    /// <summary>
    /// Alias the store identity onto the app account (sign-in) or back off it (sign-out).
    /// Without this, RevenueCat stays on a per-INSTALL anonymous id: the same person on a
    /// phone and a tablet looks like two customers, and someone who bought before ever
    /// making an account would lose the subscription the moment they made one.
    ///
    /// <para><b>Login aliases the anonymous id onto the account id.</b> Whether the purchase
    /// travels with it is governed by the RevenueCat dashboard's <i>transfer behavior</i>
    /// setting, not by this code: verify it in sandbox before release, because the failure
    /// mode is a paying customer silently losing access at the exact moment they sign in.</para>
    ///
    /// <para>Logout on an already-anonymous user is an error in the SDK, not a problem,
    /// swallowed here, since "no account to leave" is the expected state for most users.</para>
    /// </summary>
    public async Task IdentifyAsync(string? accountId)
    {
        if (!_configured)
        {
            try { await InitializeAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[Billing] identify: init failed: {ex.Message}"); return; }
        }

        try
        {
            CustomerInfoDto? info;
            if (!string.IsNullOrEmpty(accountId))
            {
                if (_rc.GetAppUserId() == accountId)
                    return;   // already this account: Login again would be a no-op round trip
                info = await WithTimeout(_rc.Login(accountId, CancellationToken.None),
                    StoreCallTimeoutSeconds, (CustomerInfoDto?)null);
            }
            else
            {
                if (_rc.IsAnonymous())
                    return;   // nothing to leave
                info = await WithTimeout(_rc.Logout(CancellationToken.None),
                    StoreCallTimeoutSeconds, (CustomerInfoDto?)null);
            }

            LogCustomerInfo(accountId is null ? "after logout" : "after login", info);
            if (info is null)
                return;   // timed out: leave the entitlement as it was; resume will re-check

            SetEntitlement(IsPremiumActive(info));
            _entitlementKnown = true;
        }
        catch (Exception ex)
        {
            // Identity is an optimization, never a gate. A failure here must not cost a
            // paying user their access, so the previous entitlement simply stands.
            Debug.WriteLine($"[Billing] identify failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Write the creator code onto the RevenueCat identity as subscriber attributes, so it
    /// rides along on every purchase event this install later produces: anonymous installs
    /// included, which is the entire reason this path exists alongside the server-side
    /// record.
    ///
    /// <para>Two keys are set together: <c>$campaign</c>, RevenueCat's reserved attribution
    /// attribute (so their own charts segment by it without any work on our side), and a
    /// plain <c>creator_code</c>, which is a custom key and therefore always accepted. If a
    /// future SDK ever rejects the reserved key, the custom one still carries the value and
    /// the server-side record in migration 0016 is unaffected either way.</para>
    ///
    /// <para>Best effort by contract. This is a marketing number; nothing reads it as a
    /// gate, so a failure is logged and dropped rather than surfaced.</para>
    /// </summary>
    public async Task SetAttributionAsync(string? creatorCode)
    {
        if (!_configured)
        {
            try { await InitializeAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[Billing] attribution: init failed: {ex.Message}"); return; }
        }

        try
        {
            var value = creatorCode ?? string.Empty;   // empty clears the attribute
            _rc.SetAttributes(new Dictionary<string, string>
            {
                ["creator_code"] = value,
                ["$campaign"] = value,
            });
            Debug.WriteLine($"[Billing] attribution set: '{value}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Billing] attribution failed: {ex.Message}");
        }
    }

    private async Task RefreshEntitlementAsync()
    {
        var info = await WithTimeout(_rc.GetCustomerInfo(), StoreCallTimeoutSeconds, (CustomerInfoDto?)null);
        LogCustomerInfo("customer info", info);
        if (info is null)
            return; // timed out: leave the entitlement "unknown" so the gate stays optimistic
        SetEntitlement(IsPremiumActive(info));
        _entitlementKnown = true;
    }

    /// <summary>Whether the user has full access per RevenueCat. Prefers an exact match on
    /// the configured entitlement id; falls back to "any active entitlement", because this
    /// app has a single paid tier = full access, so a dashboard rename or an
    /// identifier-vs-display-name mismatch must never silently re-lock a paying user. The
    /// fallback logs, so a mismatch is visible rather than silent (M2).</summary>
    private static bool IsPremiumActive(CustomerInfoDto? info)
    {
        if (info is null || info.Entitlements.Count == 0)
            return false;
        if (info.Entitlements.Any(e => e.Identifier == BillingConfig.EntitlementId && e.IsActive))
            return true;
        var anyActive = info.Entitlements.Any(e => e.IsActive);
        if (anyActive)
            Debug.WriteLine(
                $"[Billing] entitlement '{BillingConfig.EntitlementId}' not found, but another " +
                "entitlement is active: granting via fallback. Align BillingConfig.EntitlementId " +
                "with the dashboard identifier to remove this fallback.");
        return anyActive;
    }

    /// <summary>Run a store call with an upper time bound so the UI can't hang on a stalled
    /// network. Returns <paramref name="onTimeout"/> if it doesn't finish in time; rethrows
    /// the call's own exception if it faults.</summary>
    private static async Task<T> WithTimeout<T>(Task<T> task, int seconds, T onTimeout)
    {
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
        if (done != task)
        {
            Debug.WriteLine($"[Billing] a store call timed out after {seconds}s");
            return onTimeout;
        }
        return await task;
    }

    /// <summary>Dump what RevenueCat reports so a "paid but still locked" case is
    /// diagnosable from logcat: the entitlements it knows about (id + active) and the raw
    /// active subscription ids. Debug builds only.</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private static void LogCustomerInfo(string context, CustomerInfoDto? info)
    {
        if (info is null)
        {
            Debug.WriteLine($"[Billing] {context}: <null>");
            return;
        }
        var ents = info.Entitlements.Count == 0
            ? "(none)"
            : string.Join(", ", info.Entitlements.Select(e => $"{e.Identifier}:active={e.IsActive}"));
        var subs = info.ActiveSubscriptions.Count == 0 ? "(none)" : string.Join(", ", info.ActiveSubscriptions);
        Debug.WriteLine($"[Billing] {context}: looking for entitlement '{BillingConfig.EntitlementId}' | entitlements=[{ents}] | activeSubscriptions=[{subs}]");
    }

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
